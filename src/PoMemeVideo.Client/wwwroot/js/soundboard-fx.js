/**
 * soundboard-fx.js
 *
 * Meme Soundboard Waveform Synthesizer & Real-time Web Audio DSP Rack.
 * Powers audio-reactive frequency spectrum / oscilloscope visualization
 * and live DSP effects (8-bit Bitcrusher, pitch/speed slider, sub-bass boost).
 */
(function (global) {
    "use strict";

    class SoundboardFxEngine {
        constructor() {
            this.canvas = null;
            this.ctx = null;
            this.audioCtx = null;
            this.analyser = null;
            this.activeSource = null;
            this.currentAudioEl = null;

            // DSP settings
            this.bitcrusherEnabled = false;
            this.bassBoostEnabled = false;
            this.playbackRate = 1.0;

            this.bassFilter = null;
            this.bitcrushShaper = null;
            this.animFrame = null;
            this.onEndedCallback = null;
        }

        init(canvasId) {
            this.canvas = document.getElementById(canvasId);
            if (!this.canvas) return;
            this.ctx = this.canvas.getContext("2d");

            this.resize();
            window.removeEventListener("resize", this._onResize);
            this._onResize = () => this.resize();
            window.addEventListener("resize", this._onResize);

            this._ensureAudioContext();
            this._startVisualizerLoop();
        }

        resize() {
            if (!this.canvas) return;
            const rect = this.canvas.parentElement ? this.canvas.parentElement.getBoundingClientRect() : { width: 500, height: 70 };
            this.canvas.width = rect.width;
            this.canvas.height = 70;
        }

        _ensureAudioContext() {
            if (!this.audioCtx) {
                const AudioCtx = window.AudioContext || window.webkitAudioContext;
                if (!AudioCtx) return;
                this.audioCtx = new AudioCtx();

                this.analyser = this.audioCtx.createAnalyser();
                this.analyser.fftSize = 256;
                this.analyser.smoothingTimeConstant = 0.75;

                // Bass boost low-shelf filter (+8dB at 60Hz)
                this.bassFilter = this.audioCtx.createBiquadFilter();
                this.bassFilter.type = "lowshelf";
                this.bassFilter.frequency.setValueAtTime(65, this.audioCtx.currentTime);
                this.bassFilter.gain.setValueAtTime(this.bassBoostEnabled ? 8 : 0, this.audioCtx.currentTime);

                // Bitcrusher waveshaper (quantization curve)
                this.bitcrushShaper = this.audioCtx.createWaveShaper();
                this._updateBitcrushCurve();

                this.bassFilter.connect(this.bitcrushShaper);
                this.bitcrushShaper.connect(this.analyser);
                this.analyser.connect(this.audioCtx.destination);
            }
        }

        _updateBitcrushCurve() {
            if (!this.bitcrushShaper) return;
            const samples = 1024;
            const curve = new Float32Array(samples);
            const bits = this.bitcrusherEnabled ? 4 : 16;
            const steps = Math.pow(2, bits);

            for (let i = 0; i < samples; i++) {
                const x = (i * 2) / samples - 1;
                // Quantize to steps
                curve[i] = Math.round(x * steps) / steps;
            }
            this.bitcrushShaper.curve = curve;
        }

        setBitcrusher(enabled) {
            this.bitcrusherEnabled = !!enabled;
            this._updateBitcrushCurve();
            if (global.cyberAudio) global.cyberAudio.playToggle(this.bitcrusherEnabled);
        }

        setBassBoost(enabled) {
            this.bassBoostEnabled = !!enabled;
            if (this.bassFilter && this.audioCtx) {
                this.bassFilter.gain.setValueAtTime(this.bassBoostEnabled ? 8 : 0, this.audioCtx.currentTime);
            }
            if (global.cyberAudio) global.cyberAudio.playToggle(this.bassBoostEnabled);
        }

        setPlaybackRate(rate) {
            this.playbackRate = Math.max(0.5, Math.min(2.0, parseFloat(rate) || 1.0));
            if (this.currentAudioEl) {
                this.currentAudioEl.playbackRate = this.playbackRate;
            }
            if (global.cyberAudio) {
                global.cyberAudio.playSliderTick((this.playbackRate - 0.5) / 1.5);
            }
        }

        playAudio(audioId, onEnded) {
            this._ensureAudioContext();
            if (this.audioCtx && this.audioCtx.state === "suspended") {
                this.audioCtx.resume();
            }

            const audioEl = document.getElementById(audioId);
            if (!audioEl) return;

            // Stop any currently playing sound
            this.stopAll();

            this.currentAudioEl = audioEl;
            this.onEndedCallback = onEnded;
            audioEl.playbackRate = this.playbackRate;

            // Connect MediaElementSourceNode if not already hooked
            if (!audioEl.__soundboardHooked && this.audioCtx) {
                try {
                    const source = this.audioCtx.createMediaElementSource(audioEl);
                    source.connect(this.bassFilter);
                    audioEl.__soundboardHooked = true;
                } catch (e) {
                    // Falls back to direct audio element output if cross-origin or already connected
                }
            }

            audioEl.currentTime = 0;
            audioEl.play().catch(() => {});

            audioEl.onended = () => {
                this.currentAudioEl = null;
                if (typeof this.onEndedCallback === "function") {
                    this.onEndedCallback();
                }
            };
        }

        stopAll() {
            if (this.currentAudioEl) {
                this.currentAudioEl.pause();
                this.currentAudioEl.currentTime = 0;
                this.currentAudioEl = null;
            }
            document.querySelectorAll("audio").forEach((a) => {
                a.pause();
                a.currentTime = 0;
            });
        }

        _startVisualizerLoop() {
            const freqData = new Uint8Array(64);
            const timeData = new Uint8Array(128);

            const render = () => {
                if (this.ctx && this.canvas) {
                    const ctx = this.ctx;
                    const w = this.canvas.width;
                    const h = this.canvas.height;
                    ctx.clearRect(0, 0, w, h);

                    // Background grid
                    ctx.fillStyle = "rgba(4, 10, 8, 0.9)";
                    ctx.fillRect(0, 0, w, h);
                    ctx.strokeStyle = "rgba(0, 245, 106, 0.25)";
                    ctx.lineWidth = 1;
                    ctx.strokeRect(0, 0, w, h);

                    if (this.analyser && this.currentAudioEl && !this.currentAudioEl.paused) {
                        this.analyser.getByteFrequencyData(freqData);
                        this.analyser.getByteTimeDomainData(timeData);

                        // 1. Frequency Spectrum Bars (Matrix Green phosphor)
                        const barCount = 32;
                        const barWidth = (w / barCount) - 2;
                        for (let i = 0; i < barCount; i++) {
                            const val = freqData[i] / 255.0;
                            const barHeight = val * (h - 10);
                            const x = i * (barWidth + 2) + 1;
                            const y = h - barHeight - 2;

                            ctx.save();
                            ctx.fillStyle = i > 24 ? "#ff007f" : (i > 16 ? "#00ffdc" : "#00ff41");
                            ctx.shadowColor = ctx.fillStyle;
                            ctx.shadowBlur = 6;
                            ctx.fillRect(x, y, barWidth, barHeight);
                            ctx.restore();
                        }

                        // 2. Oscilloscope Waveform overlay
                        ctx.save();
                        ctx.strokeStyle = "#ffffff";
                        ctx.lineWidth = 1.5;
                        ctx.shadowColor = "#00ffdc";
                        ctx.shadowBlur = 8;
                        ctx.beginPath();
                        const sliceWidth = w / timeData.length;
                        let ox = 0;
                        for (let i = 0; i < timeData.length; i++) {
                            const v = timeData[i] / 128.0;
                            const oy = (v * h) * 0.5;
                            if (i === 0) ctx.moveTo(ox, oy);
                            else ctx.lineTo(ox, oy);
                            ox += sliceWidth;
                        }
                        ctx.stroke();
                        ctx.restore();
                    } else {
                        // Idle flatline with slow breathing scanline
                        ctx.strokeStyle = "rgba(0, 255, 65, 0.4)";
                        ctx.lineWidth = 1;
                        ctx.beginPath();
                        ctx.moveTo(0, h * 0.5);
                        ctx.lineTo(w, h * 0.5);
                        ctx.stroke();

                        ctx.font = "9px 'Courier New', monospace";
                        ctx.fillStyle = "rgba(0, 255, 65, 0.5)";
                        ctx.textAlign = "center";
                        ctx.fillText("// MEME DSP RACK: READY — AUDITION ANY SOUND BELOW", w * 0.5, h * 0.5 - 6);
                    }
                }

                this.animFrame = requestAnimationFrame(render);
            };

            this.animFrame = requestAnimationFrame(render);
        }

        destroy() {
            if (this.animFrame) cancelAnimationFrame(this.animFrame);
            window.removeEventListener("resize", this._onResize);
        }
    }

    global.soundboardFx = new SoundboardFxEngine();
})(window);

