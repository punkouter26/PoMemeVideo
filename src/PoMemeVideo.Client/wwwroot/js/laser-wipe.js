/**
 * laser-wipe.js
 *
 * WebGL2/Canvas Interactive Laser-Wipe Before & After Split Player.
 * Replaces side-by-side video containers with a unified interactive wipe canvas
 * featuring an electric plasma neon seam and velocity-scaled electrostatic crackle audio.
 */
(function (global) {
    "use strict";

    class LaserWipePlayer {
        constructor() {
            this.canvas = null;
            this.ctx = null;
            this.sourceVideo = null;
            this.memeVideo = null;
            this.splitNorm = 0.5; // 0.0 to 1.0
            this.isDragging = false;
            this.animFrame = null;
            this.lastX = 0;
            this.lastTime = 0;
            this.plasmaSparks = [];
        }

        init(canvasId, sourceVideoId, memeVideoId) {
            this.canvas = document.getElementById(canvasId);
            this.sourceVideo = document.getElementById(sourceVideoId);
            this.memeVideo = document.getElementById(memeVideoId);

            if (!this.canvas || !this.sourceVideo || !this.memeVideo) return;
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
            const rect = this.canvas.parentElement ? this.canvas.parentElement.getBoundingClientRect() : { width: 640, height: 360 };
            this.canvas.width = rect.width;
            this.canvas.height = rect.height;
        }

        _setupEvents() {
            const updateSplit = (clientX) => {
                const rect = this.canvas.getBoundingClientRect();
                const x = Math.max(0, Math.min(rect.width, clientX - rect.left));
                this.splitNorm = x / rect.width;

                // Calculate velocity for electrostatic audio
                const now = performance.now();
                const dt = Math.max(1, now - this.lastTime);
                const dx = Math.abs(x - this.lastX);
                const velocity = (dx / dt) * 10; // normalized speed
                this.lastX = x;
                this.lastTime = now;

                if (global.cyberAudio && velocity > 0.5) {
                    const intensity = Math.min(1.0, velocity * 0.15);
                    global.cyberAudio.playElectrostatic(intensity);
                }

                // Add plasma electric sparks
                const seamY = Math.random() * this.canvas.height;
                this.plasmaSparks.push({
                    x: x + (Math.random() - 0.5) * 6,
                    y: seamY,
                    vx: (Math.random() - 0.5) * 80,
                    vy: (Math.random() - 0.5) * 80,
                    life: 0.35 + Math.random() * 0.25,
                    color: Math.random() > 0.5 ? "#00ffdc" : "#ffe600"
                });
            };

            const onMove = (e) => {
                if (!this.isDragging) return;
                const clientX = e.touches ? e.touches[0].clientX : e.clientX;
                updateSplit(clientX);
            };

            const onEnd = () => {
                this.isDragging = false;
            };

            this.canvas.addEventListener("mousedown", (e) => {
                this.isDragging = true;
                updateSplit(e.clientX);
            });

            window.addEventListener("mousemove", onMove);
            window.addEventListener("mouseup", onEnd);

            this.canvas.addEventListener("touchstart", (e) => {
                this.isDragging = true;
                updateSplit(e.touches[0].clientX);
            }, { passive: true });

            window.addEventListener("touchmove", onMove, { passive: true });
            window.addEventListener("touchend", onEnd);
        }

        _startLoop() {
            let lastTime = performance.now();

            const render = (now) => {
                const dt = Math.min((now - lastTime) / 1000, 0.05);
                lastTime = now;

                if (this.ctx && this.canvas) {
                    const ctx = this.ctx;
                    const w = this.canvas.width;
                    const h = this.canvas.height;
                    const splitX = this.splitNorm * w;

                    ctx.clearRect(0, 0, w, h);

                    // 1. Draw Meme Output (Right / Underneath)
                    if (this.memeVideo && this.memeVideo.readyState >= 2) {
                        ctx.drawImage(this.memeVideo, 0, 0, w, h);
                    } else {
                        ctx.fillStyle = "#050e0a";
                        ctx.fillRect(0, 0, w, h);
                        ctx.fillStyle = "#00ff41";
                        ctx.font = "12px 'Courier New', monospace";
                        ctx.fillText("[ WAITING FOR AI MEME VIDEO... ]", w * 0.5, h * 0.5);
                    }

                    // 2. Draw Source Video (Left / Clipped)
                    if (this.sourceVideo && this.sourceVideo.readyState >= 2 && splitX > 0) {
                        ctx.save();
                        ctx.beginPath();
                        ctx.rect(0, 0, splitX, h);
                        ctx.clip();
                        ctx.drawImage(this.sourceVideo, 0, 0, w, h);
                        ctx.restore();
                    }

                    // 3. Electric Laser Seam
                    ctx.save();
                    // Glow outer
                    ctx.strokeStyle = "rgba(0, 255, 220, 0.4)";
                    ctx.lineWidth = 8;
                    ctx.beginPath();
                    ctx.moveTo(splitX, 0);
                    ctx.lineTo(splitX, h);
                    ctx.stroke();

                    // Core bright beam
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 2;
                    ctx.shadowColor = "#00ffdc";
                    ctx.shadowBlur = 12;
                    ctx.beginPath();
                    ctx.moveTo(splitX, 0);
                    ctx.lineTo(splitX, h);
                    ctx.stroke();

                    // Drag Handle diamond in center
                    const handleY = h * 0.5;
                    ctx.fillStyle = "#030706";
                    ctx.strokeStyle = "#00ffdc";
                    ctx.lineWidth = 2;
                    ctx.beginPath();
                    ctx.arc(splitX, handleY, 16, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.stroke();

                    // Handle arrows
                    ctx.font = "11px 'Courier New', monospace";
                    ctx.fillStyle = "#00ffdc";
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillText("◀ ⚡ ▶", splitX, handleY);

                    // Labels on sides
                    ctx.font = "10px 'Courier New', monospace";
                    ctx.fillStyle = "rgba(255, 255, 255, 0.75)";
                    ctx.shadowColor = "#000000";
                    ctx.shadowBlur = 4;
                    ctx.textAlign = "left";
                    ctx.fillText("ORIGINAL", 12, 22);
                    ctx.textAlign = "right";
                    ctx.fillText("AI MEME", w - 12, 22);
                    ctx.restore();

                    // 4. Update & Render Plasma Sparks
                    for (let i = this.plasmaSparks.length - 1; i >= 0; i--) {
                        const s = this.plasmaSparks[i];
                        s.x += s.vx * dt;
                        s.y += s.vy * dt;
                        s.life -= dt;
                        if (s.life <= 0) {
                            this.plasmaSparks.splice(i, 1);
                            continue;
                        }

                        ctx.save();
                        ctx.globalAlpha = s.life * 2;
                        ctx.fillStyle = s.color;
                        ctx.shadowColor = s.color;
                        ctx.shadowBlur = 6;
                        ctx.beginPath();
                        ctx.arc(s.x, s.y, 2, 0, Math.PI * 2);
                        ctx.fill();
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

    global.laserWipePlayer = new LaserWipePlayer();
})(window);

