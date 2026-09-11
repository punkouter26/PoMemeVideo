/**
 * browser-llm.js  —  Transformers.js wrapper for in-browser LLM inference.
 *
 * Loaded as an ES module via index.html.
 * Models are downloaded and cached directly in the user's browser via
 * the browser's standard Cache API (WebGPU execution).
 */
import { pipeline, env } from 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@3';

const LOCAL_MODELS_ROOT = '/models/';

env.allowLocalModels = true;
env.allowRemoteModels = true;
env.useBrowserCache = true;

const DEFAULT_MODEL_ID = 'smollm2-360m-instruct-onnx';
const MAX_SOUNDS_IN_PROMPT = 80;
const MAX_VISION_LABELS_IN_PROMPT = 64;

const MODEL_REGISTRY = {
    'smollm2-360m-instruct-onnx': {
        label: 'SmolLM2 360M',
        repo: 'onnx-community/SmolLM2-360M-Instruct-ONNX',
        dtype: 'q4f16',
        sizeMb: 250,
        notes: 'SmolLM2 360M Instruct quantized for lightweight WebGPU execution.',
    },
    'qwen2.5-0.5b-instruct': {
        label: 'Qwen 2.5 0.5B',
        repo: 'onnx-community/Qwen2.5-0.5B-Instruct',
        dtype: 'q4f16',
        sizeMb: 350,
        notes: 'Qwen2.5 0.5B Instruct quantized for fast WebGPU inference.',
    },
    'qwen2.5-1.5b-instruct': {
        label: 'Qwen 2.5 1.5B',
        repo: 'onnx-community/Qwen2.5-1.5B-Instruct',
        dtype: 'q4f16',
        sizeMb: 980,
        notes: 'Qwen2.5 1.5B Instruct quantized for high quality WebGPU execution.',
    },
    'phi-1_5-dev': {
        label: 'Phi 1.5',
        repo: 'onnx-community/Phi-1_5-dev',
        dtype: 'q4',
        sizeMb: 850,
        notes: 'Phi-1.5 Dev quantized ONNX.',
    },
    'gemma-2-2b-jpn-it': {
        label: 'Gemma 2 2B',
        repo: 'onnx-community/gemma-2-2b-jpn-it',
        dtype: 'q4f16',
        sizeMb: 1500,
        notes: 'Gemma 2 2B quantized ONNX.',
    },
    'gemma-4-e2b-it-onnx': {
        label: 'Gemma 4 E2B',
        repo: 'onnx-community/gemma-4-e2b-it-onnx',
        dtype: 'q4f16',
        sizeMb: 1200,
        unsupportedReason: 'Gemma 4 architecture is not yet supported in transformers@3.',
    },
};

let _generator = null;
let _loadPromise = null;
let _currentModel = null;

function trace(event, details = {}) {
    console.info('[browser-llm]', event, details);
}

function normalizeError(error, context) {
    if (error instanceof Error) {
        return new Error(`${context}: ${error.message}`);
    }
    if (typeof error === 'number') {
        return new Error(`${context}: runtime error code ${error}`);
    }
    if (typeof error === 'string') {
        return new Error(`${context}: ${error}`);
    }
    return new Error(`${context}: ${JSON.stringify(error)}`);
}

function getModelEntry(modelId) {
    return MODEL_REGISTRY[modelId] || {
        label: modelId,
        repo: modelId,
        dtype: 'q4f16',
        sizeMb: 300,
        notes: 'Custom model',
    };
}

async function resolveModelTarget(modelId) {
    const entry = getModelEntry(modelId);
    // Check if server hosts local files under /models/{modelId}/config.json
    try {
        const localCheck = await fetch(`${LOCAL_MODELS_ROOT}${modelId}/config.json`, { method: 'HEAD' });
        if (localCheck.ok) {
            trace('model-target-local', { modelId, path: `${LOCAL_MODELS_ROOT}${modelId}` });
            return { target: `${LOCAL_MODELS_ROOT}${modelId}`, isLocal: true };
        }
    } catch {
        // Fall back to remote Hugging Face repo
    }
    return { target: entry.repo || modelId, isLocal: false };
}

async function isModelCached(modelId) {
    const key = `pmv_model_cached_${modelId}`;
    if (localStorage.getItem(key) === 'true') {
        return true;
    }
    const entry = getModelEntry(modelId);
    if (!entry || !entry.repo) return false;

    if ('caches' in window) {
        try {
            const cacheNames = await caches.keys();
            for (const name of cacheNames) {
                const cache = await caches.open(name);
                const requests = await cache.keys();
                const matched = requests.some(req =>
                    req.url.includes(entry.repo) && (req.url.includes('.onnx') || req.url.includes('config.json'))
                );
                if (matched) {
                    localStorage.setItem(key, 'true');
                    return true;
                }
            }
        } catch {
            // cache read failure
        }
    }
    return false;
}

// ONNX Runtime's WebGPU kernels do not support every quantisation on every GPU/driver combination.
// q4f16 (4-bit weights with fp16 compute) in particular aborts on some adapters, while plain q4
// works. Rather than hard-failing the model, try the preferred dtype first, then fall back.
const DTYPE_FALLBACK_CHAIN = ['q4f16', 'q4', 'q8'];
const dtypeStorageKey = (modelId) => `pmv_model_dtype_${modelId}`;

function buildDtypeCandidates(modelId, preferred) {
    // A dtype that already worked for this model on this machine wins over the registry default,
    // so a runtime that rejects q4f16 does not have to re-download and re-fail on every load.
    const remembered = localStorage.getItem(dtypeStorageKey(modelId));
    const wanted = preferred ? [preferred] : [];
    return Array.from(new Set([...wanted, ...(remembered ? [remembered] : []), ...DTYPE_FALLBACK_CHAIN]));
}

async function createPipelineWithProgress(modelId, progressCallback = null) {
    if (!navigator.gpu) {
        throw new Error('WebGPU is not supported in this browser. Please use Chrome 113+, Edge 113+, or a WebGPU-enabled browser.');
    }

    const entry = getModelEntry(modelId);
    if (entry.unsupportedReason) {
        throw new Error(entry.unsupportedReason);
    }

    const { target, isLocal } = await resolveModelTarget(modelId);

    // transformers.js resolves a repo id against env.localModelPath whenever allowLocalModels is on.
    // For a remote repo that makes it request /models/<repo>/... from our own origin, 404, and never
    // fall back to the Hugging Face host. Only allow local resolution when weights actually exist
    // under LOCAL_MODELS_ROOT for this model.
    env.allowLocalModels = isLocal;

    const startedAt = performance.now();
    const dtypes = buildDtypeCandidates(modelId, entry.dtype || 'q4f16');

    trace('model-load-start', { modelId, target, isLocal, dtypes });

    let lastError = null;

    for (const dtype of dtypes) {
        try {
            const generator = await pipeline('text-generation', target, {
                dtype,
                device: 'webgpu',
                progress_callback: progressCallback,
            });

            localStorage.setItem(`pmv_model_cached_${modelId}`, 'true');
            localStorage.setItem(dtypeStorageKey(modelId), dtype);

            trace('model-load-success', {
                modelId,
                target,
                dtype,
                elapsedMs: Math.round(performance.now() - startedAt),
            });

            return generator;
        } catch (error) {
            lastError = error;
            trace('model-load-dtype-failure', {
                modelId,
                target,
                dtype,
                error: error instanceof Error ? error.message : String(error),
            });
        }
    }

    trace('model-load-failure', {
        modelId,
        target,
        dtypesTried: dtypes,
        error: lastError instanceof Error ? lastError.message : String(lastError),
    });

    throw normalizeError(lastError, `Failed to load model '${modelId}' (${target}) on WebGPU`);
}

async function ensureLoaded(modelId, progressCallback = null) {
    if (_generator && _currentModel === modelId) return _generator;

    if (!_loadPromise || _currentModel !== modelId) {
        _loadPromise = createPipelineWithProgress(modelId, progressCallback)
            .then(gen => {
                _generator = gen;
                _currentModel = modelId;
                return gen;
            })
            .catch(error => {
                _loadPromise = null;
                _generator = null;
                _currentModel = null;
                throw normalizeError(error, 'BrowserLLM model load failed');
            });
    }

    return await _loadPromise;
}

function getVisionTimestampSeconds(label) {
    const value = label?.timestampSeconds ?? label?.TimestampSeconds;
    return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function getVisionLabelText(label) {
    return label?.label ?? label?.Label ?? 'unknown';
}

function getSoundId(sound) {
    return sound?.soundId ?? sound?.SoundId ?? 'unknown';
}

function getSoundName(sound) {
    return sound?.displayName ?? sound?.DisplayName ?? 'unknown';
}

function getSoundTags(sound) {
    const tags = sound?.tags ?? sound?.Tags ?? [];
    return Array.isArray(tags) ? tags : [];
}

function buildMessages(payload) {
    const visionLabels = (payload.visionLabels || []).slice(0, MAX_VISION_LABELS_IN_PROMPT);
    const sounds = (payload.sounds || []).slice(0, MAX_SOUNDS_IN_PROMPT);

    const labelsText = visionLabels
        .map(v => `  t=${getVisionTimestampSeconds(v).toFixed(1)}s  label="${getVisionLabelText(v)}"`)
        .join('\n');

    const soundsText = sounds
        .map(s => `  id="${getSoundId(s)}"  name="${getSoundName(s)}"  tags=[${getSoundTags(s).join(', ')}]`)
        .join('\n');

    const system =
        'You are a meme video director. ' +
        'Given action labels from a video and available meme sounds, ' +
        'output ONLY a valid JSON array mapping sounds to timestamps. ' +
        'Each element: {"timestampMs":number,"soundId":"uuid","actionVectorTags":["tag"],' +
        '"selectionRationale":"short reason","isIronic":false,"visualEffect":"None","effectIntensity":0.5}. ' +
        'visualEffect must be one of: None, DeepFry, SnapZoom, MotionBlur, Overlay. ' +
        'Output raw JSON only — no markdown fences, no extra text.';

    const user =
        `Action labels:\n${labelsText}\n\nAvailable sounds (top ${sounds.length}):\n${soundsText}\n\nDirector's Script JSON:`;

    return [
        { role: 'system', content: system },
        { role: 'user',   content: user   },
    ];
}

function extractJson(rawText) {
    let text = rawText.trim();
    const fenceStart = text.indexOf('```');
    if (fenceStart !== -1) {
        const afterFence = text.indexOf('\n', fenceStart);
        text = afterFence !== -1 ? text.slice(afterFence + 1) : text.slice(fenceStart + 3);
    }
    const fenceEnd = text.lastIndexOf('```');
    if (fenceEnd !== -1) text = text.slice(0, fenceEnd);

    const arrayStart = text.indexOf('[');
    const arrayEnd   = text.lastIndexOf(']');
    if (arrayStart === -1 || arrayEnd === -1) return '[]';
    return text.slice(arrayStart, arrayEnd + 1);
}

window.browserLLM = {
    /**
     * Checks whether WebGPU is available and which models are currently cached in the browser.
     */
    async checkAllCacheStatus() {
        const hasGpu = Boolean(navigator.gpu);
        const cached = {};
        for (const id of Object.keys(MODEL_REGISTRY)) {
            cached[id] = await isModelCached(id);
        }
        return {
            hasWebGpu: hasGpu,
            cachedModels: cached,
        };
    },

    /**
     * Checks if a single model is already cached in the browser.
     */
    async isModelCached(modelId) {
        return await isModelCached(modelId || DEFAULT_MODEL_ID);
    },

    /**
     * Downloads and caches a model directly in the browser with live progress callbacks to Blazor.
     */
    async downloadModel(modelId, dotNetHelper) {
        const selectedModelId = modelId || DEFAULT_MODEL_ID;
        let lastNotifyTime = 0;

        const progressCallback = (info) => {
            const now = performance.now();
            if (now - lastNotifyTime > 50 || info.status === 'done' || info.status === 'ready') {
                lastNotifyTime = now;
                if (dotNetHelper) {
                    try {
                        dotNetHelper.invokeMethodAsync('OnDownloadProgress', {
                            status: info.status || 'downloading',
                            file: info.file || '',
                            progress: typeof info.progress === 'number' ? Math.round(info.progress) : 0,
                            loadedBytes: info.loaded || 0,
                            totalBytes: info.total || 0,
                        });
                    } catch {
                        // ignore if connection dropped
                    }
                }
            }
        };

        const generator = await createPipelineWithProgress(selectedModelId, progressCallback);
        _generator = generator;
        _currentModel = selectedModelId;

        if (dotNetHelper) {
            try {
                dotNetHelper.invokeMethodAsync('OnDownloadComplete', selectedModelId);
            } catch {
                // ignore
            }
        }
        return true;
    },

    /**
     * Clears cached weights for a specific model from Cache Storage and localStorage.
     */
    async clearCache(modelId) {
        const selectedModelId = modelId || DEFAULT_MODEL_ID;
        const entry = getModelEntry(selectedModelId);
        localStorage.removeItem(`pmv_model_cached_${selectedModelId}`);

        if ('caches' in window && entry.repo) {
            try {
                const cacheNames = await caches.keys();
                for (const name of cacheNames) {
                    const cache = await caches.open(name);
                    const requests = await cache.keys();
                    for (const req of requests) {
                        if (req.url.includes(entry.repo)) {
                            await cache.delete(req);
                        }
                    }
                }
            } catch (e) {
                console.warn('[browser-llm] clearCache error:', e);
            }
        }

        if (_currentModel === selectedModelId) {
            _generator = null;
            _currentModel = null;
            _loadPromise = null;
        }
        return true;
    },

    /**
     * Called by Blazor Engine.razor when a BrowserLLMInferenceRequest arrives via SignalR.
     * Returns the director script entries as a JSON string.
     */
    async generate(payloadJson) {
        const startedAt = performance.now();

        try {
            const payload = JSON.parse(payloadJson);
            const selectedModelId = payload.modelId || DEFAULT_MODEL_ID;
            trace('generate-start', {
                sessionId: payload.sessionId,
                modelId: selectedModelId,
                visionLabelCount: Array.isArray(payload.visionLabels) ? payload.visionLabels.length : 0,
                soundCount: Array.isArray(payload.sounds) ? payload.sounds.length : 0,
            });

            const gen = await ensureLoaded(selectedModelId);
            const messages = buildMessages(payload);

            trace('generate-inference-start', {
                sessionId: payload.sessionId,
                modelId: selectedModelId,
                messageCount: messages.length,
                promptChars: messages.map(m => m.content?.length ?? 0).reduce((sum, len) => sum + len, 0),
            });

            const output = await gen(messages, {
                max_new_tokens: 512,
                temperature: 0.4,
                do_sample: true,
                return_full_text: false,
            });

            const rawText = output?.[0]?.generated_text ?? '[]';
            trace('generate-success', {
                sessionId: payload.sessionId,
                modelId: selectedModelId,
                elapsedMs: Math.round(performance.now() - startedAt),
                rawTextLength: rawText.length,
            });
            return extractJson(rawText);
        } catch (error) {
            trace('generate-failure', {
                elapsedMs: Math.round(performance.now() - startedAt),
                error: error instanceof Error ? error.message : String(error),
            });
            throw normalizeError(error, 'BrowserLLM generate failed');
        }
    },

    /** Warm up the model in the background immediately after page load. */
    async warmup(modelId) {
        try { await ensureLoaded(modelId); } catch { /* non-fatal */ }
    },

    /**
     * Diagnostics-only probe to compare model load behavior across webgpu/wasm.
     */
    async probeModel(modelId) {
        const selectedModelId = modelId || DEFAULT_MODEL_ID;
        const entry = getModelEntry(selectedModelId);
        const diagnostics = {
            modelId: selectedModelId,
            entry,
            isCached: await isModelCached(selectedModelId),
            attempts: [],
        };

        if (!navigator.gpu) {
            diagnostics.attempts.push({
                device: 'webgpu',
                status: 'skipped',
                reason: 'WebGPU unavailable in this browser.',
            });
            return diagnostics;
        }

        if (entry.unsupportedReason) {
            diagnostics.attempts.push({
                device: 'webgpu',
                status: 'error',
                error: entry.unsupportedReason,
            });
            return diagnostics;
        }

        const startedAt = performance.now();
        try {
            const generator = await createPipelineWithProgress(selectedModelId);
            diagnostics.attempts.push({
                device: 'webgpu',
                status: 'loaded',
                elapsedMs: Math.round(performance.now() - startedAt),
            });
            if (typeof generator?.dispose === 'function') {
                try { generator.dispose(); } catch { /* noop */ }
            }
        } catch (error) {
            diagnostics.attempts.push({
                device: 'webgpu',
                status: 'error',
                elapsedMs: Math.round(performance.now() - startedAt),
                error: error instanceof Error ? error.message : String(error),
            });
        }

        return diagnostics;
    },
};
