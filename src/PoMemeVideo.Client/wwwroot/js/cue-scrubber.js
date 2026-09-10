/**
 * cue-scrubber.js
 *
 * Interactive Holographic Scrub Bar & Granular Audio Scrubbing for Reveal.razor Cue Studio.
 * Renders color-coded cue markers, magnetic snapping, and real-time audio scrub feedback.
 */
(function (global) {
    "use strict";

    class CueScrubber {
        constructor() {
            this.canvas = null;
            this.ctx = null;
            this.video = null;
            this.duration = 10;
            this.cues = [];
            this.onSeek = null;
            this.isDragging = false;
            this.hoverTime = null;
            this.animFrame = null;
            this.lastTickTime = 0;

            this.effectColors = {
                SnapZoom: "#00ffdc",
                DeepFry: "#ff6600",
                MotionBlur: "#bf00ff",
                Overlay: "#ffe600",
                Default: "#00ff41"
            };
        }

        init(canvasId, videoId, durationSec, cuesJson, dotNetHelper) {
            this.canvas = document.getElementById(canvasId);
            this.video = document.getElementById(videoId);
            this.duration = Math.max(1, durationSec || (this.video ? this.video.duration : 10) || 10);
            this.cues = Array.isArray(cuesJson) ? cuesJson : [];
            this.dotNetHelper = dotNetHelper;

            if (!this.canvas) return;
            this.ctx = this.canvas.getContext("2d");

            this.resize();
            window.removeEventListener("resize", this._onResize);
            this._onResize = () => this.resize();
            window.addEventListener("resize", this._onResize);

            this._setupEvents();
            this._startLoop();
        }

        resize() {
            if (!this.canvas) return;
            const rect = this.canvas.parentElement ? this.canvas.parentElement.getBoundingClientRect() : { width: 500, height: 44 };
            this.canvas.width = rect.width;
            this.canvas.height = 44;
        }

        updateCues(cuesJson) {
            this.cues = Array.isArray(cuesJson) ? cuesJson : [];
        }

        updateDuration(dur) {
            if (dur > 0) this.duration = dur;
        }

        _setupEvents() {
            const getPos = (e) => {
                const rect = this.canvas.getBoundingClientRect();
                const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                const x = Math.max(0, Math.min(rect.width, clientX - rect.left));
                return (x / rect.width) * this.duration;
            };

            const handleSeek = (timeSec, isFinal) => {
                // Magnetic snap check: snap to cue within 150ms
                let seekTime = timeSec;
                for (let i = 0; i < this.cues.length; i++) {
                    const cueSec = (this.cues[i].timestampMs || 0) / 1000.0;
                    if (Math.abs(seekTime - cueSec) < 0.15) {
                        seekTime = cueSec;
                        break;
                    }
                }

                if (this.video && !isNaN(seekTime)) {
                    this.video.currentTime = seekTime;
                }

                // Granular audio tick on scrub
                const now = performance.now();
                if (now - this.lastTickTime > 40) {
                    this.lastTickTime = now;
                    if (global.cyberAudio) {
                        global.cyberAudio.playSliderTick(seekTime / this.duration);
                    }
                }

                if (isFinal && this.dotNetHelper) {
                    try {
                        this.dotNetHelper.invokeMethodAsync("OnTimelineScrubbed", Math.round(seekTime * 1000));
                    } catch (e) {}
                }
            };

            this.canvas.addEventListener("mousedown", (e) => {
                this.isDragging = true;
                handleSeek(getPos(e), false);
            });

            window.addEventListener("mousemove", (e) => {
                if (!this.canvas) return;
                const rect = this.canvas.getBoundingClientRect();
                if (e.clientX >= rect.left && e.clientX <= rect.right &&
                    e.clientY >= rect.top && e.clientY <= rect.bottom) {
                    this.hoverTime = getPos(e);
                } else {
                    this.hoverTime = null;
                }

                if (this.isDragging) {
                    handleSeek(getPos(e), false);
                }
            });

            window.addEventListener("mouseup", (e) => {
                if (this.isDragging) {
                    this.isDragging = false;
                    handleSeek(getPos(e), true);
                }
            });

            // Touch support
            this.canvas.addEventListener("touchstart", (e) => {
                this.isDragging = true;
                handleSeek(getPos(e), false);
            }, { passive: true });

            window.addEventListener("touchmove", (e) => {
                if (this.isDragging) {
                    handleSeek(getPos(e), false);
                }
            }, { passive: true });

            window.addEventListener("touchend", (e) => {
                if (this.isDragging) {
                    this.isDragging = false;
                    handleSeek(getPos(e), true);
                }
            });
        }

        _startLoop() {
            const render = () => {
                if (this.ctx && this.canvas) {
                    const ctx = this.ctx;
                    const w = this.canvas.width;
                    const h = this.canvas.height;
                    ctx.clearRect(0, 0, w, h);

                    // Background timeline bar
                    ctx.fillStyle = "rgba(10, 18, 16, 0.85)";
                    ctx.fillRect(0, 0, w, h);

                    // Top & bottom neon boundary lines
                    ctx.strokeStyle = "rgba(0, 245, 106, 0.4)";
                    ctx.lineWidth = 1;
                    ctx.strokeRect(0, 0, w, h);

                    // Grid tick lines every second
                    const tickStep = this.duration > 30 ? 5 : (this.duration > 15 ? 2 : 1);
                    for (let t = 0; t <= this.duration; t += tickStep) {
                        const x = (t / this.duration) * w;
                        ctx.strokeStyle = "rgba(0, 245, 106, 0.18)";
                        ctx.beginPath();
                        ctx.moveTo(x, h - 8);
                        ctx.lineTo(x, h);
                        ctx.stroke();

                        if (t % (tickStep * 2) === 0) {
                            ctx.font = "9px 'Courier New', monospace";
                            ctx.fillStyle = "rgba(0, 245, 106, 0.6)";
                            ctx.textAlign = "center";
                            ctx.fillText(`${t}s`, x, h - 11);
                        }
                    }

                    // Render Cues
                    this.cues.forEach((c, idx) => {
                        const cueSec = (c.timestampMs || 0) / 1000.0;
                        const x = (cueSec / this.duration) * w;
                        const effect = c.visualEffect || "Default";
                        const color = this.effectColors[effect] || this.effectColors.Default;

                        ctx.save();
                        // Marker line
                        ctx.strokeStyle = color;
                        ctx.lineWidth = 2;
                        ctx.shadowColor = color;
                        ctx.shadowBlur = 8;
                        ctx.beginPath();
                        ctx.moveTo(x, 2);
                        ctx.lineTo(x, h - 2);
                        ctx.stroke();

                        // Diamond flag head
                        ctx.fillStyle = color;
                        ctx.beginPath();
                        ctx.moveTo(x, 2);
                        ctx.lineTo(x - 5, 8);
                        ctx.lineTo(x, 14);
                        ctx.lineTo(x + 5, 8);
                        ctx.closePath();
                        ctx.fill();

                        // Hit index label
                        ctx.font = "8px 'Courier New', monospace";
                        ctx.fillStyle = "#ffffff";
                        ctx.textAlign = "center";
                        ctx.fillText(`${idx + 1}`, x, 9);

                        ctx.restore();
                    });

                    // Current playback position
                    const curTime = this.video ? this.video.currentTime : 0;
                    const curX = (curTime / this.duration) * w;

                    // Progress fill
                    ctx.fillStyle = "rgba(0, 255, 220, 0.12)";
                    ctx.fillRect(0, 0, curX, h);

                    // Scrubber Playhead Laser Needle
                    ctx.save();
                    ctx.strokeStyle = "#00ffdc";
                    ctx.shadowColor = "#00ffdc";
                    ctx.shadowBlur = 12;
                    ctx.lineWidth = 2.5;
                    ctx.beginPath();
                    ctx.moveTo(curX, 0);
                    ctx.lineTo(curX, h);
                    ctx.stroke();

                    // Playhead needle head
                    ctx.fillStyle = "#ffffff";
                    ctx.beginPath();
                    ctx.arc(curX, h * 0.5, 4.5, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.restore();

                    // Hover Time Tooltip
                    if (this.hoverTime !== null && !this.isDragging) {
                        const hx = (this.hoverTime / this.duration) * w;
                        ctx.save();
                        ctx.fillStyle = "rgba(0, 0, 0, 0.85)";
                        ctx.strokeStyle = "#00ffdc";
                        ctx.lineWidth = 1;
                        const tipText = `${this.hoverTime.toFixed(2)}s`;
                        ctx.font = "10px 'Courier New', monospace";
                        const textW = ctx.measureText(tipText).width + 8;
                        const tipX = Math.max(textW / 2, Math.min(w - textW / 2, hx));
                        ctx.fillRect(tipX - textW / 2, 2, textW, 14);
                        ctx.strokeRect(tipX - textW / 2, 2, textW, 14);

                        ctx.fillStyle = "#00ffdc";
                        ctx.textAlign = "center";
                        ctx.fillText(tipText, tipX, 13);
                        ctx.restore();
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

    global.cueScrubber = new CueScrubber();
})(window);

