/**
 * browser-llm.js  —  Transformers.js wrapper for in-browser LLM inference.
 *
 * Loaded as an ES module via index.html.
 * Blazor calls window.browserLLM.generate(payloadJson) via JSInterop.
 *
 * Model: onnx-community/SmolLM2-360M-Instruct (ONNX q4, ~180 MB on first load,
 *        cached in the browser's Cache Storage thereafter).
 */
import { pipeline, env } from 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@3';

// Serve ONNX files directly from Hugging Face CDN — no local proxy needed.
env.allowLocalModels = false;

let _generator = null;
let _loadPromise = null;
let _currentModel = null;

async function ensureLoaded(modelId) {
    if (_generator && _currentModel === modelId) return _generator;

    // Deduplicate concurrent load calls
    if (!_loadPromise || _currentModel !== modelId) {
        _currentModel = modelId;
        _loadPromise = pipeline('text-generation', modelId, {
            dtype: 'q4',          // 4-bit quantised — fastest cold-start
            device: 'webgpu',     // falls back to wasm if WebGPU unavailable
        }).catch(() =>
            // WebGPU failed — retry on wasm
            pipeline('text-generation', modelId, { dtype: 'q4', device: 'wasm' })
        );
    }

    _generator = await _loadPromise;
    return _generator;
}

function buildMessages(payload) {
    const { visionLabels, sounds } = payload;

    const labelsText = visionLabels
        .map(v => `  t=${v.timestampSeconds.toFixed(1)}s  label="${v.label}"`)
        .join('\n');

    const soundsText = sounds
        .map(s => `  id="${s.soundId}"  name="${s.displayName}"  tags=[${(s.tags || []).join(', ')}]`)
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
        `Action labels:\n${labelsText}\n\nAvailable sounds:\n${soundsText}\n\nDirector's Script JSON:`;

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
        const payload = JSON.parse(payloadJson);
        const gen = await ensureLoaded(payload.modelId || 'onnx-community/SmolLM2-360M-Instruct');
        const messages = buildMessages(payload);

        const output = await gen(messages, {
            max_new_tokens: 512,
            temperature: 0.4,
            do_sample: true,
            return_full_text: false,
        });

        const rawText = output?.[0]?.generated_text ?? '[]';
        return extractJson(rawText);
    },

    /** Warm up the model in the background immediately after page load. */
    async warmup(modelId) {
        try { await ensureLoaded(modelId); } catch { /* non-fatal */ }
    },
};
