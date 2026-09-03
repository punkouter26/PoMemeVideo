/**
 * browser-llm.js  —  Transformers.js wrapper for in-browser LLM inference.
 *
 * Loaded as an ES module via index.html.
 * Blazor calls window.browserLLM.generate(payloadJson) via JSInterop.
 *
 * Model: loaded from local /models/{modelId} assets only.
 */
import { pipeline, env } from 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@3';

const LOCAL_MODELS_ROOT = '/models/';

env.allowLocalModels = true;
env.allowRemoteModels = false;
env.localModelPath = LOCAL_MODELS_ROOT;

const DEFAULT_MODEL_ID = 'smollm2-360m-instruct-onnx';
const MAX_SOUNDS_IN_PROMPT = 80;
const MAX_VISION_LABELS_IN_PROMPT = 64;

const MODEL_LOAD_PROFILES = {
    'qwen2.5-0.5b-instruct-q4': {
        dtype: 'q4',
        expectedFiles: ['config.json', 'onnx/model_q4.onnx'],
        notes: 'Qwen2.5 0.5B Instruct quantized INT4 for fast WebGPU inference.',
    },
    'smollm2-360m-instruct-onnx': {
        dtype: 'q4f16',
        expectedFiles: ['config.json', 'onnx/model_q4f16.onnx'],
        notes: 'SmolLM2 360M Instruct quantized for lightweight WebGPU execution.',
    },
    'phi-1_5-dev': {
        dtype: 'q4',
        expectedFiles: ['config.json', 'onnx/model_q4.onnx'],
        notes: 'Phi q4 profile expects onnx/model_q4.onnx for transformers.js loader compatibility.',
    },
    'gemma-4-e2b-it-onnx': {
        dtype: 'q4f16',
        expectedFiles: ['config.json', 'onnx/decoder_model_merged_q4f16.onnx'],
        unsupportedReason:
            'Gemma 4 E2B ONNX bundle is not supported by transformers@3 text-generation pipeline (model_type=gemma4).',
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

function getModelLoadProfile(modelId) {
    const profile = MODEL_LOAD_PROFILES[modelId] || {};
    return {
        dtype: profile.dtype || 'q4f16',
        expectedFiles: profile.expectedFiles || ['config.json', 'onnx/model_q4f16.onnx'],
        unsupportedReason: profile.unsupportedReason,
        notes: profile.notes,
    };
}

async function createPipeline(modelId, device, dtype) {
    const startedAt = performance.now();

    try {
        trace('model-load-start', {
            modelId,
            device,
            dtype,
            localPath: `${env.localModelPath}${modelId}`,
        });

        const generator = await pipeline('text-generation', modelId, {
            dtype,
            device,
        });

        trace('model-load-success', {
            modelId,
            device,
            elapsedMs: Math.round(performance.now() - startedAt),
        });

        return generator;
    } catch (error) {
        trace('model-load-failure', {
            modelId,
            device,
            elapsedMs: Math.round(performance.now() - startedAt),
            error: error instanceof Error ? error.message : String(error),
        });

        throw normalizeError(
            error,
            `Failed to load selected model '${modelId}' on ${device} (dtype=${dtype}, localPath=${env.localModelPath}${modelId})`
        );
    }
}

async function checkExpectedAssets(modelId) {
    const profile = getModelLoadProfile(modelId);
    const results = [];

    for (const relativePath of profile.expectedFiles) {
        const url = `${LOCAL_MODELS_ROOT}${modelId}/${relativePath}`;
        try {
            const response = await fetch(url, { method: 'HEAD' });
            results.push({ path: relativePath, status: response.status });
        } catch (error) {
            results.push({
                path: relativePath,
                status: -1,
                error: error instanceof Error ? error.message : String(error),
            });
        }
    }

    return results;
}

async function assertExpectedAssets(modelId) {
    const assets = await checkExpectedAssets(modelId);
    const missing = assets.filter(asset => asset.status !== 200);

    if (missing.length > 0) {
        const expected = missing.map(asset => `${LOCAL_MODELS_ROOT}${modelId}/${asset.path}`).join(', ');
        throw new Error(
            `Model '${modelId}' is missing required local asset(s): ${expected}. ` +
            'Provide these files in the MODEL folder or choose another local model.'
        );
    }
}

async function loadLocalWebGpuModel(modelId) {
    if (!navigator.gpu)
        throw new Error('WebGPU is required for local BrowserLLM models on this app.');

    const selectedModelId = modelId || DEFAULT_MODEL_ID;
    const profile = getModelLoadProfile(selectedModelId);

    if (profile.unsupportedReason) {
        throw new Error(profile.unsupportedReason);
    }

    trace('model-profile', {
        modelId: selectedModelId,
        dtype: profile.dtype,
        notes: profile.notes,
    });

    await assertExpectedAssets(selectedModelId);

    const generator = await createPipeline(selectedModelId, 'webgpu', profile.dtype);
    _currentModel = selectedModelId;
    return generator;
}

async function ensureLoaded(modelId) {
    if (_generator && _currentModel === modelId) return _generator;

    // Deduplicate concurrent load calls
    if (!_loadPromise || _currentModel !== modelId) {
        _loadPromise = loadLocalWebGpuModel(modelId).catch((error) => {
            _loadPromise = null;
            _generator = null;
            _currentModel = null;
            throw normalizeError(error, 'BrowserLLM model load failed');
        });
    }

    _generator = await _loadPromise;
    trace('model-ready', {
        requestedModelId: modelId,
        activeModelId: _currentModel,
    });
    return _generator;
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
    // Strip leading assistant turn prefix and markdown fences if the model added them
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
        const profile = getModelLoadProfile(selectedModelId);
        const diagnostics = {
            modelId: selectedModelId,
            profile,
            assets: await checkExpectedAssets(selectedModelId),
            attempts: [],
        };

        const devices = ['webgpu', 'wasm'];

        for (const device of devices) {
            if (device === 'webgpu' && !navigator.gpu) {
                diagnostics.attempts.push({
                    device,
                    status: 'skipped',
                    reason: 'WebGPU unavailable in this browser.',
                });
                continue;
            }

            if (profile.unsupportedReason) {
                diagnostics.attempts.push({
                    device,
                    status: 'error',
                    error: profile.unsupportedReason,
                });
                continue;
            }

            const startedAt = performance.now();

            try {
                const generator = await createPipeline(selectedModelId, device, profile.dtype);
                diagnostics.attempts.push({
                    device,
                    status: 'loaded',
                    elapsedMs: Math.round(performance.now() - startedAt),
                });

                if (typeof generator?.dispose === 'function') {
                    try { generator.dispose(); } catch { /* noop */ }
                }
            } catch (error) {
                diagnostics.attempts.push({
                    device,
                    status: 'error',
                    elapsedMs: Math.round(performance.now() - startedAt),
                    error: error instanceof Error ? error.message : String(error),
                });
            }
        }

        return diagnostics;
    },
};
