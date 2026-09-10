/**
 * dropzone-physics.js
 *
 * Quantum ASCII & Meme-Fluid Gravitational Particle Engine for AsciiDropZone.
 * Renders an interactive physics particle vortex with ASCII glyphs and meme stickers
 * attracted gravitationally to the mouse cursor and dropzone center.
 */
(function (global) {
    "use strict";

    class DropzonePhysicsEngine {
        constructor() {
            this.canvas = null;
            this.ctx = null;
            this.dropzoneEl = null;
            this.particles = [];
            this.shockwaves = [];
            this.animFrame = null;
            this.isDragging = false;
            this.mouseX = 0;
            this.mouseY = 0;
            this.centerX = 0;
            this.centerY = 0;
            this.lastDopplerTime = 0;
            this.stickers = [];

            this.asciiChars = ["[ ]", "*", "+", "▓", "░", "0", "1", "⬡", "⚡", "MEME", "404", "AI", "◈"];
            this.colors = ["#00ff41", "#00ffdc", "#4ce9a6", "#39ff14", "#ffffff", "#ffe600"];

            this._loadStickers();
        }

        _loadStickers() {
            const stickerNames = ["deal-with-it", "laser-eyes", "clown-wig"];
            stickerNames.forEach((name) => {
                const img = new Image();
                img.src = `/overlays/${name}.png`;
                img.onload = () => this.stickers.push(img);
            });
        }

        init(canvasId, dropzoneId) {
            this.canvas = document.getElementById(canvasId);
            this.dropzoneEl = document.getElementById(dropzoneId);
            if (!this.canvas || !this.dropzoneEl) return;

            this.ctx = this.canvas.getContext("2d");
            this.resize();

            window.removeEventListener("resize", this._onResize);
            this._onResize = () => this.resize();
            window.addEventListener("resize", this._onResize);

            this._setupWindowDragListeners();
            this._startLoop();
        }

        resize() {
            if (!this.canvas || !this.dropzoneEl) return;
            const rect = this.dropzoneEl.getBoundingClientRect();
            this.canvas.width = rect.width;
            this.canvas.height = rect.height;
            this.centerX = rect.width * 0.5;
            this.centerY = rect.height * 0.5;
        }

        _setupWindowDragListeners() {
            const onDragOver = (e) => {
                if (!this.canvas || !this.dropzoneEl) return;
                const rect = this.canvas.getBoundingClientRect();
                this.mouseX = e.clientX - rect.left;
                this.mouseY = e.clientY - rect.top;

                if (!this.isDragging) {
                    this.isDragging = true;
                    this._spawnVortexParticles(40);
                }

                // Calculate proximity to center (0.0 to 1.0)
                const dx = this.mouseX - this.centerX;
                const dy = this.mouseY - this.centerY;
                const dist = Math.sqrt(dx * dx + dy * dy);
                const maxDist = Math.sqrt(this.centerX * this.centerX + this.centerY * this.centerY);
                const proximity = Math.max(0, 1 - dist / maxDist);

                const now = performance.now();
                if (now - this.lastDopplerTime > 90) {
                    this.lastDopplerTime = now;
                    if (global.cyberAudio) {
                        global.cyberAudio.playDoppler(proximity);
                    }
                }
            };

            const onDragLeave = (e) => {
                if (e.clientX <= 0 || e.clientY <= 0 ||
                    e.clientX >= window.innerWidth || e.clientY >= window.innerHeight) {
                    this.isDragging = false;
                }
            };

            window.addEventListener("dragover", onDragOver);
            window.addEventListener("dragleave", onDragLeave);
            window.addEventListener("drop", () => {
                this.isDragging = false;
            });
        }

        _spawnVortexParticles(count) {
            for (let i = 0; i < count; i++) {
                const angle = Math.random() * Math.PI * 2;
                const radius = 100 + Math.random() * 200;
                const isSticker = this.stickers.length > 0 && Math.random() < 0.25;

                this.particles.push({
                    x: this.centerX + Math.cos(angle) * radius,
                    y: this.centerY + Math.sin(angle) * radius,
                    vx: -Math.sin(angle) * (2 + Math.random() * 3),
                    vy: Math.cos(angle) * (2 + Math.random() * 3),
                    char: this.asciiChars[Math.floor(Math.random() * this.asciiChars.length)],
                    color: this.colors[Math.floor(Math.random() * this.colors.length)],
                    size: 10 + Math.random() * 6,
                    life: 2.5 + Math.random() * 1.5,
                    maxLife: 4.0,
                    alpha: 0.9,
                    isSticker: isSticker,
                    stickerImg: isSticker ? this.stickers[Math.floor(Math.random() * this.stickers.length)] : null,
                    rotation: Math.random() * Math.PI * 2,
                    rotSpeed: (Math.random() - 0.5) * 4
                });
            }
        }

        triggerDropImpact(x, y) {
            const originX = x !== undefined ? x : this.centerX;
            const originY = y !== undefined ? y : this.centerY;

            // Spawn shockwaves
            this.shockwaves.push({
                x: originX,
                y: originY,
                radius: 10,
                maxRadius: Math.max(this.canvas.width, this.canvas.height) * 0.8,
                speed: 450,
                alpha: 1.0,
                color: "#00ffdc"
            });
            this.shockwaves.push({
                x: originX,
                y: originY,
                radius: 5,
                maxRadius: Math.max(this.canvas.width, this.canvas.height) * 0.6,
                speed: 280,
                alpha: 0.8,
                color: "#39ff14"
            });

            // Explosive burst
            for (let i = 0; i < 50; i++) {
                const angle = Math.random() * Math.PI * 2;
                const speed = 120 + Math.random() * 380;
                const isSticker = this.stickers.length > 0 && Math.random() < 0.35;

                this.particles.push({
                    x: originX,
                    y: originY,
                    vx: Math.cos(angle) * speed,
                    vy: Math.sin(angle) * speed,
                    char: this.asciiChars[Math.floor(Math.random() * this.asciiChars.length)],
                    color: this.colors[Math.floor(Math.random() * this.colors.length)],
                    size: 12 + Math.random() * 8,
                    life: 1.8 + Math.random() * 1.2,
                    maxLife: 3.0,
                    alpha: 1.0,
                    isSticker: isSticker,
                    stickerImg: isSticker ? this.stickers[Math.floor(Math.random() * this.stickers.length)] : null,
                    rotation: Math.random() * Math.PI * 2,
                    rotSpeed: (Math.random() - 0.5) * 8
                });
            }

            if (global.cyberAudio) {
                global.cyberAudio.playSubBassDrop();
                global.cyberAudio.playClick(1.5);
            }
        }

        _startLoop() {
            let lastTime = performance.now();

            const update = (now) => {
                const dt = Math.min((now - lastTime) / 1000, 0.05);
                lastTime = now;

                if (this.ctx && this.canvas) {
                    this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);

                    // Ambient particle spawn when idle
                    if (!this.isDragging && this.particles.length < 12 && Math.random() < 0.08) {
                        this.particles.push({
                            x: Math.random() * this.canvas.width,
                            y: this.canvas.height + 10,
                            vx: (Math.random() - 0.5) * 15,
                            vy: -(25 + Math.random() * 35),
                            char: this.asciiChars[Math.floor(Math.random() * this.asciiChars.length)],
                            color: "#00ff41",
                            size: 9 + Math.random() * 4,
                            life: 3.0,
                            maxLife: 3.0,
                            alpha: 0.4,
                            isSticker: false,
                            rotation: 0,
                            rotSpeed: (Math.random() - 0.5) * 0.5
                        });
                    }

                    // Update & Render Shockwaves
                    for (let i = this.shockwaves.length - 1; i >= 0; i--) {
                        const sw = this.shockwaves[i];
                        sw.radius += sw.speed * dt;
                        sw.alpha -= dt * 1.5;
                        if (sw.alpha <= 0 || sw.radius >= sw.maxRadius) {
                            this.shockwaves.splice(i, 1);
                            continue;
                        }

                        this.ctx.save();
                        this.ctx.beginPath();
                        this.ctx.arc(sw.x, sw.y, sw.radius, 0, Math.PI * 2);
                        this.ctx.lineWidth = 4 * sw.alpha;
                        this.ctx.strokeStyle = sw.color;
                        this.ctx.shadowColor = sw.color;
                        this.ctx.shadowBlur = 15;
                        this.ctx.globalAlpha = sw.alpha;
                        this.ctx.stroke();
                        this.ctx.restore();
                    }

                    // Target for gravitational pull
                    const targetX = this.isDragging ? this.mouseX : this.centerX;
                    const targetY = this.isDragging ? this.mouseY : this.centerY;

                    // Update & Render Particles
                    for (let i = this.particles.length - 1; i >= 0; i--) {
                        const p = this.particles[i];
                        p.life -= dt;
                        if (p.life <= 0) {
                            this.particles.splice(i, 1);
                            continue;
                        }

                        // Gravitational attraction toward target
                        if (this.isDragging) {
                            const dx = targetX - p.x;
                            const dy = targetY - p.y;
                            const dist = Math.max(15, Math.sqrt(dx * dx + dy * dy));
                            const force = 4500 / (dist * dist + 100);

                            // Gravitational acceleration + orbital tangential spin
                            p.vx += (dx / dist) * force * 15 * dt - (dy / dist) * force * 12 * dt;
                            p.vy += (dy / dist) * force * 15 * dt + (dx / dist) * force * 12 * dt;

                            // Drag dampening
                            p.vx *= 0.96;
                            p.vy *= 0.96;
                        }

                        p.x += p.vx * dt;
                        p.y += p.vy * dt;
                        p.rotation += p.rotSpeed * dt;
                        const lifeRatio = p.life / p.maxLife;
                        const alpha = Math.min(1.0, lifeRatio * 1.5) * p.alpha;

                        this.ctx.save();
                        this.ctx.translate(p.x, p.y);
                        this.ctx.rotate(p.rotation);
                        this.ctx.globalAlpha = alpha;

                        if (p.isSticker && p.stickerImg && p.stickerImg.complete) {
                            const sz = p.size * 2.2;
                            this.ctx.drawImage(p.stickerImg, -sz / 2, -sz / 2, sz, sz);
                        } else {
                            this.ctx.font = `${p.size}px 'Courier New', monospace`;
                            this.ctx.fillStyle = p.color;
                            this.ctx.shadowColor = p.color;
                            this.ctx.shadowBlur = 8;
                            this.ctx.textAlign = "center";
                            this.ctx.textBaseline = "middle";
                            this.ctx.fillText(p.char, 0, 0);
                        }

                        this.ctx.restore();
                    }
                }

                this.animFrame = requestAnimationFrame(update);
            };

            this.animFrame = requestAnimationFrame(update);
        }

        destroy() {
            if (this.animFrame) cancelAnimationFrame(this.animFrame);
            window.removeEventListener("resize", this._onResize);
        }
    }

    global.dropzonePhysics = new DropzonePhysicsEngine();
})(window);

