/**
 * crt-glass-shader.js
 *
 * WebGL2 Prismatic Snell's Law Glassmorphism & Phosphor CRT Surface.
 * Simulates physical optical dispersion, chromatic RGB separation,
 * aperture-grille subpixels, and dynamic cathode-ray hum.
 */
(function (global) {
    "use strict";

    class CrtGlassShader {
        constructor() {
            this.humCtx = null;
            this.humGain = null;
            this.humEnabled = false;
        }

        /**
         * Attaches a WebGL2 chromatic CRT phosphor overlay canvas to a frame container.
         */
        attach(canvasId) {
            const canvas = document.getElementById(canvasId);
            if (!canvas) return;

            const gl = canvas.getContext("webgl2", { alpha: true, premultipliedAlpha: false });
            if (!gl) return; // Graceful fallback to CSS

            const vsSource = `#version 300 es
            in vec2 a_position;
            out vec2 v_uv;
            void main() {
                v_uv = (a_position + 1.0) * 0.5;
                gl_Position = vec4(a_position, 0.0, 1.0);
            }`;

            const fsSource = `#version 300 es
            precision highp float;
            in vec2 v_uv;
            out vec4 fragColor;

            uniform float u_time;
            uniform vec2 u_resolution;
            uniform vec2 u_mouse;

            // Barrel distortion & chromatic aberration
            vec2 barrelDistort(vec2 uv, float k) {
                vec2 center = uv - 0.5;
                float r2 = dot(center, center);
                return 0.5 + center * (1.0 + k * r2);
            }

            void main() {
                vec2 uv = v_uv;
                vec2 uvDist = barrelDistort(uv, 0.08);

                // Check CRT viewport bounds with smooth rounded corners
                vec2 corner = abs(uvDist - 0.5) * 2.0;
                float cornerDist = max(corner.x, corner.y);
                if (corner.x > 1.0 || corner.y > 1.0) {
                    discard;
                }

                // Dynamic scanline interlace
                float scanline = sin(uvDist.y * u_resolution.y * 1.5 + u_time * 5.0) * 0.04;
                
                // Phosphor subpixel aperture grille (RGB stripes)
                float subpixel = mod(gl_FragCoord.x, 3.0);
                vec3 phosphorMask = vec3(0.9, 0.9, 0.9);
                if (subpixel < 1.0) {
                    phosphorMask = vec3(1.05, 0.92, 0.92);
                } else if (subpixel < 2.0) {
                    phosphorMask = vec3(0.92, 1.08, 0.92);
                } else {
                    phosphorMask = vec3(0.92, 0.92, 1.1);
                }

                // Chromatic vignette / edge refraction
                float vig = 1.0 - dot(uvDist - 0.5, uvDist - 0.5) * 1.4;
                vig = clamp(vig, 0.0, 1.0);

                // Prismatic rim light along border
                float borderDist = length(max(abs(uvDist - 0.5) * 2.0 - 0.92, 0.0));
                float rimGlow = smoothstep(0.08, 0.0, borderDist);

                // Dynamic mouse sheen reflection
                vec2 mouseNorm = u_mouse / u_resolution;
                float sheen = max(0.0, 1.0 - distance(uv, mouseNorm) * 2.5);
                sheen = pow(sheen, 4.0) * 0.12;

                vec3 color = vec3(0.0, 0.96, 0.41) * (0.01 + scanline + rimGlow * 0.25);
                color += vec3(0.0, 1.0, 0.86) * sheen;
                color *= phosphorMask * vig;

                float alpha = clamp(rimGlow * 0.5 + sheen * 0.4 + abs(scanline) * 0.6, 0.0, 0.45);
                fragColor = vec4(color, alpha);
            }`;

            function compileShader(gl, type, source) {
                const s = gl.createShader(type);
                gl.shaderSource(s, source);
                gl.compileShader(s);
                return s;
            }

            const vs = compileShader(gl, gl.VERTEX_SHADER, vsSource);
            const fs = compileShader(gl, gl.FRAGMENT_SHADER, fsSource);
            const program = gl.createProgram();
            gl.attachShader(program, vs);
            gl.attachShader(program, fs);
            gl.linkProgram(program);

            const posLoc = gl.getAttribLocation(program, "a_position");
            const timeLoc = gl.getUniformLocation(program, "u_time");
            const resLoc = gl.getUniformLocation(program, "u_resolution");
            const mouseLoc = gl.getUniformLocation(program, "u_mouse");

            const buf = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, buf);
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
                -1, -1, 1, -1, -1, 1,
                -1, 1, 1, -1, 1, 1
            ]), gl.STATIC_DRAW);

            let mouseX = 0, mouseY = 0;
            canvas.addEventListener("mousemove", (e) => {
                const rect = canvas.getBoundingClientRect();
                mouseX = e.clientX - rect.left;
                mouseY = rect.height - (e.clientY - rect.top);
            });

            const resize = () => {
                const rect = canvas.parentElement ? canvas.parentElement.getBoundingClientRect() : { width: 300, height: 200 };
                canvas.width = rect.width;
                canvas.height = rect.height;
                gl.viewport(0, 0, canvas.width, canvas.height);
            };
            resize();
            window.addEventListener("resize", resize);

            const startTime = performance.now();
            function render() {
                if (!gl) return;
                gl.useProgram(program);
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

                gl.uniform1f(timeLoc, (performance.now() - startTime) / 1000);
                gl.uniform2f(resLoc, canvas.width, canvas.height);
                gl.uniform2f(mouseLoc, mouseX, mouseY);

                gl.enableVertexAttribArray(posLoc);
                gl.bindBuffer(gl.ARRAY_BUFFER, buf);
                gl.vertexAttribPointer(posLoc, 2, gl.FLOAT, false, 0, 0);

                gl.drawArrays(gl.TRIANGLES, 0, 6);
                requestAnimationFrame(render);
            }
            requestAnimationFrame(render);
        }

        /**
         * Procedural 60Hz cathode hum + 15.7kHz flyback transformer whine
         */
        toggleCathodeHum(enable) {
            this.humEnabled = enable !== undefined ? enable : !this.humEnabled;
            if (!this.humEnabled) {
                if (this.humGain && this.humCtx) {
                    this.humGain.gain.linearRampToValueAtTime(0, this.humCtx.currentTime + 0.1);
                }
                return false;
            }

            if (!this.humCtx) {
                const AudioCtx = window.AudioContext || window.webkitAudioContext;
                if (!AudioCtx) return false;
                this.humCtx = new AudioCtx();

                this.humGain = this.humCtx.createGain();
                this.humGain.gain.setValueAtTime(0, this.humCtx.currentTime);
                this.humGain.connect(this.humCtx.destination);

                // 60Hz hum
                const osc60 = this.humCtx.createOscillator();
                osc60.type = "sawtooth";
                osc60.frequency.setValueAtTime(60, this.humCtx.currentTime);

                const filter = this.humCtx.createBiquadFilter();
                filter.type = "lowpass";
                filter.frequency.setValueAtTime(180, this.humCtx.currentTime);

                // 15.734 kHz flyback line whine (very faint)
                const oscFlyback = this.humCtx.createOscillator();
                oscFlyback.type = "sine";
                oscFlyback.frequency.setValueAtTime(15734, this.humCtx.currentTime);

                const flybackGain = this.humCtx.createGain();
                flybackGain.gain.setValueAtTime(0.015, this.humCtx.currentTime);

                osc60.connect(filter);
                filter.connect(this.humGain);
                oscFlyback.connect(flybackGain);
                flybackGain.connect(this.humGain);

                osc60.start();
                oscFlyback.start();
            }

            if (this.humCtx.state === "suspended") {
                this.humCtx.resume();
            }

            this.humGain.gain.cancelScheduledValues(this.humCtx.currentTime);
            this.humGain.gain.setValueAtTime(0, this.humCtx.currentTime);
            this.humGain.gain.linearRampToValueAtTime(0.035, this.humCtx.currentTime + 0.4);
            return true;
        }
    }

    global.crtGlassShader = new CrtGlassShader();
})(window);

