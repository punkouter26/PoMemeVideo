/**
 * neural-core.js
 *
 * 3D Holographic "Neural Core" AI Director Telemetry Visualizer for Engine.razor.
 * Renders a pulsating 3D wireframe cyber-icosahedron with particle flares,
 * dynamic stage color shifts, and audio-reactive telemetry chirps.
 */
(function (global) {
    "use strict";

    class NeuralCoreVisualizer {
        constructor() {
            this.canvas = null;
            this.ctx = null;
            this.state = "connected"; // 'connected' | 'directing' | 'rendering' | 'complete'
            this.animFrame = null;
            this.pulseIntensity = 0.0;
            this.sparks = [];
            this.angleX = 0;
            this.angleY = 0;
            this.angleZ = 0;

            // Base icosahedron vertices
            const t = (1.0 + Math.sqrt(5.0)) / 2.0;
            this.baseVertices = [
                [-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0],
                [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t],
                [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]
            ].map(([x, y, z]) => {
                const len = Math.sqrt(x * x + y * y + z * z);
                return [x / len, y / len, z / len];
            });

            // Icosahedron edge indices
            this.edges = [
                [0, 11], [0, 5], [0, 1], [0, 7], [0, 10],
                [1, 5], [5, 11], [11, 10], [10, 7], [7, 1],
                [3, 9], [3, 4], [3, 2], [3, 6], [3, 8],
                [4, 9], [9, 8], [8, 6], [6, 2], [2, 4],
                [1, 9], [5, 4], [11, 2], [10, 6], [7, 8],
                [9, 5], [4, 11], [2, 10], [6, 7], [8, 1]
            ];

            this.stateColors = {
                connected: { stroke: "#00ffdc", glow: "rgba(0, 255, 220, 0.4)", speed: 1.0 },
                directing: { stroke: "#ffaa00", glow: "rgba(255, 170, 0, 0.6)", speed: 2.2 },
                rendering: { stroke: "#00ff41", glow: "rgba(0, 255, 65, 0.55)", speed: 1.8 },
                complete:  { stroke: "#ff007f", glow: "rgba(255, 0, 127, 0.7)", speed: 0.8 }
            };
        }

        init(canvasId) {
            this.canvas = document.getElementById(canvasId);
            if (!this.canvas) return;

            this.ctx = this.canvas.getContext("2d");
            this.resize();

            window.removeEventListener("resize", this._onResize);
            this._onResize = () => this.resize();
            window.addEventListener("resize", this._onResize);

            this._startLoop();
        }

        resize() {
            if (!this.canvas) return;
            const rect = this.canvas.parentElement ? this.canvas.parentElement.getBoundingClientRect() : { width: 300, height: 180 };
            this.canvas.width = Math.min(rect.width, 600);
            this.canvas.height = 180;
        }

        setState(newState) {
            if (this.stateColors[newState]) {
                this.state = newState;
                this.triggerChirp();
            }
        }

        triggerChirp() {
            this.pulseIntensity = 1.0;
            if (global.cyberAudio) {
                global.cyberAudio.playTelemetryChirp();
            }

            // Spawn sparks
            const cx = this.canvas ? this.canvas.width * 0.5 : 150;
            const cy = this.canvas ? this.canvas.height * 0.5 : 90;
            for (let i = 0; i < 15; i++) {
                const angle = Math.random() * Math.PI * 2;
                const spd = 40 + Math.random() * 120;
                this.sparks.push({
                    x: cx, y: cy,
                    vx: Math.cos(angle) * spd,
                    vy: Math.sin(angle) * spd,
                    life: 0.6 + Math.random() * 0.4,
                    color: this.stateColors[this.state].stroke
                });
            }
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
                    const cx = w * 0.5;
                    const cy = h * 0.5;

                    ctx.clearRect(0, 0, w, h);

                    const cfg = this.stateColors[this.state] || this.stateColors.connected;
                    const rotSpeed = cfg.speed;

                    this.angleX += 0.6 * rotSpeed * dt;
                    this.angleY += 0.8 * rotSpeed * dt;
                    this.angleZ += 0.4 * rotSpeed * dt;

                    // Fade pulse
                    this.pulseIntensity = Math.max(0, this.pulseIntensity - dt * 2.5);

                    // Radius with breathing sine + pulse flare
                    const breathe = Math.sin(now * 0.003) * 6;
                    const radius = (52 + breathe + this.pulseIntensity * 18);

                    // 3D rotation matrix
                    const radX = this.angleX;
                    const radY = this.angleY;
                    const radZ = this.angleZ;

                    const cosX = Math.cos(radX), sinX = Math.sin(radX);
                    const cosY = Math.cos(radY), sinY = Math.sin(radY);
                    const cosZ = Math.cos(radZ), sinZ = Math.sin(radZ);

                    const projVertices = this.baseVertices.map(([x, y, z]) => {
                        // Rotate Y
                        let x1 = x * cosY + z * sinY;
                        let y1 = y;
                        let z1 = -x * sinY + z * cosY;

                        // Rotate X
                        let x2 = x1;
                        let y2 = y1 * cosX - z1 * sinX;
                        let z2 = y1 * sinX + z1 * cosX;

                        // Rotate Z
                        let x3 = x2 * cosZ - y2 * sinZ;
                        let y3 = x2 * sinZ + y2 * cosZ;
                        let z3 = z2;

                        // Perspective projection
                        const fov = 3.2;
                        const pz = z3 + fov;
                        const px = cx + (x3 / pz) * radius * fov;
                        const py = cy + (y3 / pz) * radius * fov;
                        return { x: px, y: py, z: z3 };
                    });

                    // Ambient core glow
                    ctx.save();
                    const grad = ctx.createRadialGradient(cx, cy, 5, cx, cy, radius * 1.5);
                    grad.addColorStop(0, cfg.glow);
                    grad.addColorStop(1, "rgba(0, 0, 0, 0)");
                    ctx.fillStyle = grad;
                    ctx.beginPath();
                    ctx.arc(cx, cy, radius * 1.5, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.restore();

                    // Render icosahedron wireframe edges
                    ctx.save();
                    ctx.lineWidth = 1.6 + this.pulseIntensity * 1.5;
                    ctx.strokeStyle = cfg.stroke;
                    ctx.shadowColor = cfg.stroke;
                    ctx.shadowBlur = 10 + this.pulseIntensity * 15;

                    for (let i = 0; i < this.edges.length; i++) {
                        const [i1, i2] = this.edges[i];
                        const v1 = projVertices[i1];
                        const v2 = projVertices[i2];

                        // Depth fog alpha based on average z
                        const avgZ = (v1.z + v2.z) * 0.5;
                        const alpha = Math.max(0.2, (avgZ + 1.0) * 0.5);
                        ctx.globalAlpha = alpha;

                        ctx.beginPath();
                        ctx.moveTo(v1.x, v1.y);
                        ctx.lineTo(v2.x, v2.y);
                        ctx.stroke();
                    }
                    ctx.restore();

                    // Render vertices nodes
                    ctx.save();
                    for (let i = 0; i < projVertices.length; i++) {
                        const v = projVertices[i];
                        const nodeAlpha = Math.max(0.3, (v.z + 1.0) * 0.5);
                        ctx.globalAlpha = nodeAlpha;
                        ctx.fillStyle = "#ffffff";
                        ctx.shadowColor = cfg.stroke;
                        ctx.shadowBlur = 8;
                        ctx.beginPath();
                        ctx.arc(v.x, v.y, 2.5 + this.pulseIntensity * 2.0, 0, Math.PI * 2);
                        ctx.fill();
                    }
                    ctx.restore();

                    // Render sparks
                    for (let i = this.sparks.length - 1; i >= 0; i--) {
                        const s = this.sparks[i];
                        s.x += s.vx * dt;
                        s.y += s.vy * dt;
                        s.life -= dt;
                        if (s.life <= 0) {
                            this.sparks.splice(i, 1);
                            continue;
                        }

                        ctx.save();
                        ctx.globalAlpha = s.life;
                        ctx.fillStyle = s.color;
                        ctx.shadowColor = s.color;
                        ctx.shadowBlur = 6;
                        ctx.beginPath();
                        ctx.arc(s.x, s.y, 2.0, 0, Math.PI * 2);
                        ctx.fill();
                        ctx.restore();
                    }

                    // Status text readout
                    ctx.save();
                    ctx.font = "10px 'Courier New', monospace";
                    ctx.fillStyle = cfg.stroke;
                    ctx.textAlign = "center";
                    ctx.globalAlpha = 0.8;
                    ctx.fillText(`// AI NEURAL CORE: ${this.state.toUpperCase()}`, cx, h - 8);
                    ctx.restore();
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

    global.neuralCore = new NeuralCoreVisualizer();
})(window);

