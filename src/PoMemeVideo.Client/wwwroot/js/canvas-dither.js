/**
 * canvas-dither.js
 *
 * Floyd-Steinberg error-diffusion dithering on an HTMLVideoElement.
 * Reduces each frame to a 1-bit Matrix Green palette:
 *   ON  → #00FF41 (Matrix Green)
 *   OFF → #000000 (Black)
 *
 * Exports:
 *   generateDitheredFrames(videoElement, intervalSeconds)
 *     → Promise<string[]>  array of base64 PNG data URLs at each interval
 */

(function (global) {
    "use strict";

    const GREEN_R = 0x00;
    const GREEN_G = 0xFF;
    const GREEN_B = 0x41;

    /**
     * Applies Floyd-Steinberg error-diffusion dithering to a single frame.
     * Converts to luminance, then quantises to 1-bit (on/off) and maps to
     * Matrix Green or Black.
     *
     * @param {CanvasRenderingContext2D} ctx
     * @param {number} width
     * @param {number} height
     */
    function ditherFrame(ctx, width, height) {
        const imageData = ctx.getImageData(0, 0, width, height);
        const data = imageData.data;

        // Work with a floating-point luminance buffer to accumulate error
        const lum = new Float32Array(width * height);
        for (let i = 0; i < width * height; i++) {
            const r = data[i * 4];
            const g = data[i * 4 + 1];
            const b = data[i * 4 + 2];
            // Perceptual luminance (Rec. 601)
            lum[i] = 0.299 * r + 0.587 * g + 0.114 * b;
        }

        // Floyd-Steinberg error diffusion
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                const idx = y * width + x;
                const oldVal = lum[idx];
                const newVal = oldVal > 127.5 ? 255.0 : 0.0;
                const err = oldVal - newVal;
                lum[idx] = newVal;

                // Distribute error to neighbours
                if (x + 1 < width)           lum[idx + 1]         += err * 7 / 16;
                if (y + 1 < height) {
                    if (x > 0)               lum[idx + width - 1] += err * 3 / 16;
                                             lum[idx + width]     += err * 5 / 16;
                    if (x + 1 < width)       lum[idx + width + 1] += err * 1 / 16;
                }
            }
        }

        // Write back: ON → Matrix Green, OFF → Black
        for (let i = 0; i < width * height; i++) {
            const on = lum[i] > 127.5;
            data[i * 4]     = on ? GREEN_R : 0;
            data[i * 4 + 1] = on ? GREEN_G : 0;
            data[i * 4 + 2] = on ? GREEN_B : 0;
            data[i * 4 + 3] = 255; // fully opaque
        }

        ctx.putImageData(imageData, 0, 0);
    }

    /**
     * Seeks a video element to a given time and resolves once the frame is ready.
     *
     * @param {HTMLVideoElement} video
     * @param {number} time  seconds
     * @returns {Promise<void>}
     */
    function seekTo(video, time) {
        return new Promise((resolve) => {
            const onSeeked = () => {
                video.removeEventListener("seeked", onSeeked);
                video.removeEventListener("error", onError);
                clearTimeout(timeoutId);
                resolve();
            };
            const onError = () => {
                video.removeEventListener("seeked", onSeeked);
                video.removeEventListener("error", onError);
                clearTimeout(timeoutId);
                // Don't reject — caller draws whatever frame the video has so a single bad seek
                // doesn't drop the whole batch. The C# side falls back to time-based placement
                // when zero frames are extracted, so this is the right level of resilience.
                resolve();
            };
            video.addEventListener("seeked", onSeeked, { once: true });
            video.addEventListener("error", onError, { once: true });
            const timeoutId = setTimeout(onError, 3000);
            try {
                video.currentTime = time;
            } catch {
                onError();
            }
        });
    }

    /**
     * Generates dithered 1-bit Matrix Green keyframe images from a video file.
     *
     * @param {HTMLVideoElement} video        Hidden video element to use for seeking.
     * @param {number} [intervalSeconds=3]   Seconds between sampled frames.
     * @param {string} [fileInputId]         If provided, load the file via createObjectURL
     *                                        from this input id instead of using video.src.
     * @returns {Promise<string[]>}  Array of base64 PNG data URLs (one per frame).
     */
    async function generateDitheredFrames(video, intervalSeconds, fileInputId) {
        intervalSeconds = intervalSeconds || 3;

        // If a file input ID was given, create a fresh objectURL and load it
        let objectUrl = null;
        if (fileInputId) {
            const input = document.getElementById(fileInputId);
            const file = input && input.files && input.files[0];
            if (file) {
                objectUrl = URL.createObjectURL(file);
                await new Promise((resolve, reject) => {
                    let settled = false;
                    const finish = (ok) => { if (!settled) { settled = true; ok ? resolve() : reject(new Error("video load failed")); } };
                    video.addEventListener("loadedmetadata", () => finish(true), { once: true });
                    video.addEventListener("error", () => finish(false), { once: true });
                    setTimeout(() => finish(false), 10000);
                    video.src = objectUrl;
                    video.load();
                });
            }
        }

        const duration = video.duration;
        if (!isFinite(duration) || duration <= 0) {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            throw new Error("Video duration is not available. Ensure loadedmetadata has fired.");
        }

        const frameCount = Math.floor(duration / intervalSeconds);
        if (frameCount === 0) { if (objectUrl) URL.revokeObjectURL(objectUrl); return []; }

        const canvas = document.createElement("canvas");
        canvas.width = video.videoWidth || 320;
        canvas.height = video.videoHeight || 240;
        const ctx = canvas.getContext("2d", { willReadFrequently: true });

        const dataUrls = [];

        for (let i = 0; i < frameCount; i++) {
            const t = i * intervalSeconds;
            await seekTo(video, t);
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            ditherFrame(ctx, canvas.width, canvas.height);
            dataUrls.push(canvas.toDataURL("image/png"));
        }

        // Don't tear the video down — Source.razor calls captureRawFrames next, and the second
        // video.load() is what aborts the pending seeks (ERR_ABORTED on the blob: URL). The
        // Source page owns the teardown in finally so both captures share one loaded video.
        return dataUrls;
    }

    /**
     * Returns the duration of a video element by loading the given src URL.
     * Resolves with the duration in seconds, or 0 if not determinable.
     *
     * @param {HTMLVideoElement} video
     * @param {string} src  SAS URL or object URL
     * @returns {Promise<number>}
     */
    function getVideoDuration(video, src) {
        return new Promise((resolve) => {
            if (video.readyState >= 1 && isFinite(video.duration) && video.duration > 0) {
                resolve(video.duration);
                return;
            }
            let settled = false;
            const finish = (dur) => {
                if (settled) return;
                settled = true;
                video.removeEventListener("loadedmetadata", onLoaded);
                video.removeEventListener("error", onError);
                resolve(isFinite(dur) && dur > 0 ? dur : 0);
            };
            const onLoaded = () => finish(video.duration);
            const onError = () => finish(0);
            video.addEventListener("loadedmetadata", onLoaded);
            video.addEventListener("error", onError);
            // 5-second safety timeout so the Promise never hangs on ORB-blocked src
            setTimeout(() => finish(0), 5000);
            video.src = src;
            video.load();
        });
    }

    /**
     * Gets video duration by reading directly from a file input's first File object.
     * Uses createObjectURL so there are no CORS concerns.
     *
     * @param {string} inputId             The id of the file <input> element
     * @param {HTMLVideoElement} video     The hidden video element
     * @returns {Promise<number>}
     */
    function getFileDuration(inputId, video) {
        return new Promise((resolve) => {
            const input = document.getElementById(inputId);
            const file = input && input.files && input.files[0];
            if (!file) { resolve(0); return; }
            const url = URL.createObjectURL(file);
            let settled = false;
            const finish = (dur) => {
                if (settled) return;
                settled = true;
                video.removeAttribute("src");
                video.load();
                URL.revokeObjectURL(url);
                video.removeEventListener("loadedmetadata", onLoaded);
                video.removeEventListener("error", onError);
                resolve(isFinite(dur) && dur > 0 ? dur : 0);
            };
            const onLoaded = () => finish(video.duration);
            const onError = () => finish(0);
            video.addEventListener("loadedmetadata", onLoaded);
            video.addEventListener("error", onError);
            setTimeout(() => finish(0), 8000);
            video.src = url;
            video.load();
        });
    }

    // Export to global scope for JSRuntime.InvokeAsync calls from Blazor
    global.canvasDither = { generateDitheredFrames, captureRawFrames, getVideoDuration, getFileDuration, releaseVideo };

    /**
     * Captures raw (undithered) PNG frames from a video at regular intervals.
     * Used for AI vision analysis — full colour, no 1-bit processing.
     *
     * @param {string} fileInputId   ID of the <input type="file"> element
     * @param {HTMLVideoElement} video  Hidden video element to seek
     * @param {number} [intervalSeconds=3]  Seconds between sampled frames
     * @returns {Promise<string[]>}  Array of base64 PNG data URLs
     */
    async function captureRawFrames(fileInputId, video, intervalSeconds) {
        intervalSeconds = intervalSeconds || 3;

        // Source.razor calls generateDitheredFrames first, which already loaded the file into the
        // shared <video> element. Reassigning video.src and calling video.load() here would
        // abort the pending seek (net::ERR_ABORTED on the blob: URL) and yield 0 frames.
        // Reuse the existing metadata if it's available; only fall back to loading if it isn't.
        const alreadyLoaded = video.readyState >= 1
            && isFinite(video.duration)
            && video.duration > 0
            && video.videoWidth > 0;

        let ownObjectUrl = null;
        try {
            if (!alreadyLoaded) {
                const input = document.getElementById(fileInputId);
                const file = input && input.files && input.files[0];
                if (!file) throw new Error("No file selected");

                ownObjectUrl = URL.createObjectURL(file);
                await new Promise((resolve, reject) => {
                    let settled = false;
                    const finish = (ok) => { if (!settled) { settled = true; ok ? resolve() : reject(new Error("video load failed")); } };
                    video.addEventListener("loadedmetadata", () => finish(true), { once: true });
                    video.addEventListener("error", () => finish(false), { once: true });
                    setTimeout(() => finish(false), 10000);
                    video.src = ownObjectUrl;
                    video.load();
                });
            }

            const duration = video.duration;
            if (!isFinite(duration) || duration <= 0) return [];

            // Capture at 3s intervals; always at least one frame at t=0
            const times = [];
            for (let t = 0; t < duration; t += intervalSeconds) times.push(t);
            if (times.length === 0) times.push(0);

            const canvas = document.createElement("canvas");
            // Cap at 640px wide to keep payload size manageable for the AI API
            const scale = Math.min(1, 640 / (video.videoWidth || 640));
            canvas.width = Math.round((video.videoWidth || 640) * scale);
            canvas.height = Math.round((video.videoHeight || 360) * scale);
            const ctx = canvas.getContext("2d", { willReadFrequently: true });

            const dataUrls = [];
            for (const t of times) {
                await seekTo(video, t);
                ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                dataUrls.push(canvas.toDataURL("image/png"));
            }
            return dataUrls;
        } finally {
            // Only release resources we created in this call. generateDitheredFrames shares the
            // video element and tears it down itself once captureRawFrames resolves.
            if (ownObjectUrl) {
                URL.revokeObjectURL(ownObjectUrl);
            }
        }
    }

    /**
     * Detaches the <video> source so the browser can GC the blob: URL. Source.razor calls this
     * after both generateDitheredFrames and captureRawFrames have finished so the second capture
     * isn't aborted by a reassignment mid-seek.
     *
     * @param {HTMLVideoElement} video
     */
    function releaseVideo(video) {
        if (!video) return;
        try {
            video.pause();
            video.removeAttribute("src");
            video.load();
        } catch {
            // Element may already be detached — nothing to do.
        }
    }

})(window);
