/**
 * PoMemeVideo Vibe3D Engine
 *
 * Background aurora + audio-reactive visualizer + celebration burst.
 * The physics playground, parallax tilt, and FX confetti surfaces were removed
 * with the Vibe3D FX Controls widget — kept calls are initAurora, setAuroraState,
 * initAudioVisualizer, and triggerCelebrationBurst.
 */

(function () {
    'use strict';

    const Vibe3D = {
        initialized: false,
        auroraCanvas: null,
        auroraGl: null,
        auroraProgram: null,
        auroraStartTime: performance.now(),
        auroraState: 'idle',
        auroraTargetColors: {
            color1: [0.05, 0.02, 0.15],
            color2: [0.0, 0.45, 0.55],
            color3: [0.4, 0.05, 0.5],
            color4: [0.0, 0.9, 0.7]
        },
        auroraCurrentColors: {
            color1: [0.05, 0.02, 0.15],
            color2: [0.0, 0.45, 0.55],
            color3: [0.4, 0.05, 0.5],
            color4: [0.0, 0.9, 0.7]
        },

        // Audio Reactive Visualizer
        audio: {
            ctx: null,
            analyser: null,
            sourceNode: null,
            dataArray: null,
            canvas: null,
            bassLevel: 0,
            midLevel: 0,
            trebleLevel: 0
        },

        // Confetti & Shockwaves (used by triggerCelebrationBurst)
        fx: {
            canvas: null,
            ctx: null,
            particles: [],
            shockwaves: [],
            animFrame: null
        }
    };

    /* =========================================================================
       1. LIQUID AURORA & FLUID GRADIENT SHADER PASS (WebGL2)
       ========================================================================= */

    const AURORA_VS = `#version 300 es
    in vec2 a_position;
    out vec2 v_uv;
    void main() {
        v_uv = (a_position + 1.0) * 0.5;
        gl_Position = vec4(a_position, 0.0, 1.0);
    }`;

    const AURORA_FS = `#version 300 es
    precision highp float;
    in vec2 v_uv;
    out vec4 fragColor;

    uniform float u_time;
    uniform vec2 u_resolution;
    uniform vec3 u_color1;
    uniform vec3 u_color2;
    uniform vec3 u_color3;
    uniform vec3 u_color4;
    uniform float u_intensity;

    vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
    vec2 mod289(vec2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
    vec3 permute(vec3 x) { return mod289(((x*34.0)+1.0)*x); }

    float snoise(vec2 v) {
        const vec4 C = vec4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
        vec2 i  = floor(v + dot(v, C.yy) );
        vec2 x0 = v -   i + dot(i, C.xx);
        vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
        vec4 x12 = x0.xyxy + C.xxzz;
        x12.xy -= i1;
        i = mod289(i);
        vec3 p = permute( permute( i.y + vec3(0.0, i1.y, 1.0 )) + i.x + vec3(0.0, i1.x, 1.0 ));
        vec3 m = max(0.5 - vec3(dot(x0,x0), dot(x12.xy,x12.xy), dot(x12.zw,x12.zw)), 0.0);
        m = m*m;
        m = m*m;
        vec3 x = 2.0 * fract(p * C.www) - 1.0;
        vec3 h = abs(x) - 0.5;
        vec3 ox = floor(x + 0.5);
        vec3 a0 = x - ox;
        m *= 1.79284291400159 - 0.85373472095314 * ( a0*a0 + h*h );
        vec3 g;
        g.x  = a0.x  * x0.x  + h.x  * x0.y;
        g.yz = a0.yz * x12.xz + h.yz * x12.yw;
        return 130.0 * dot(m, g);
    }

    void main() {
        vec2 uv = v_uv;
        float aspect = u_resolution.x / u_resolution.y;
        vec2 p = (uv - 0.5) * vec2(aspect, 1.0);
        float t = u_time * 0.15;

        float n1 = snoise(p * 1.5 + vec2(t * 0.8, -t * 0.4));
        float n2 = snoise(p * 2.8 - vec2(-t * 0.6, t * 0.5) + vec2(n1 * 0.5));
        float n3 = snoise(p * 4.2 + vec2(n2 * 0.4, t * 0.7));

        float wave = sin(p.y * 3.0 + n1 * 2.5 + t) * 0.5 + 0.5;
        float wave2 = cos(p.x * 2.5 + n2 * 2.0 - t * 1.2) * 0.5 + 0.5;

        vec3 col = mix(u_color1, u_color2, smoothstep(0.1, 0.9, n1 * 0.5 + 0.5));
        col = mix(col, u_color3, smoothstep(0.2, 0.8, wave * n2 + 0.3));
        col = mix(col, u_color4, smoothstep(0.4, 0.95, wave2 * n3));

        float vig = 1.0 - length(uv - 0.5) * 0.8;
        col *= clamp(vig, 0.2, 1.0);
        col *= 0.88 + 0.12 * sin(uv.y * u_resolution.y * 1.5);

        fragColor = vec4(col * u_intensity, 0.95);
    }`;

    Vibe3D.initAurora = function (canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        Vibe3D.auroraCanvas = canvas;
        const gl = canvas.getContext('webgl2', { alpha: true, antialias: false, powerPreference: 'high-performance' });
        if (!gl) return;
        Vibe3D.auroraGl = gl;

        function compile(type, src) {
            const sh = gl.createShader(type);
            gl.shaderSource(sh, src);
            gl.compileShader(sh);
            return sh;
        }
        const vs = compile(gl.VERTEX_SHADER, AURORA_VS);
        const fs = compile(gl.FRAGMENT_SHADER, AURORA_FS);
        const prog = gl.createProgram();
        gl.attachShader(prog, vs);
        gl.attachShader(prog, fs);
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
            console.warn('Aurora shader link failed:', gl.getProgramInfoLog(prog));
            return;
        }
        Vibe3D.auroraProgram = prog;

        const positions = new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]);
        const buf = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);
        const aPos = gl.getAttribLocation(prog, 'a_position');
        gl.enableVertexAttribArray(aPos);
        gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

        function resize() {
            const dpr = window.devicePixelRatio || 1;
            const w = window.innerWidth;
            const h = window.innerHeight;
            canvas.width = w * dpr;
            canvas.height = h * dpr;
            canvas.style.width = w + 'px';
            canvas.style.height = h + 'px';
            gl.viewport(0, 0, canvas.width, canvas.height);
        }
        window.addEventListener('resize', resize);
        resize();

        const uTime = gl.getUniformLocation(prog, 'u_time');
        const uRes = gl.getUniformLocation(prog, 'u_resolution');
        const uC1 = gl.getUniformLocation(prog, 'u_color1');
        const uC2 = gl.getUniformLocation(prog, 'u_color2');
        const uC3 = gl.getUniformLocation(prog, 'u_color3');
        const uC4 = gl.getUniformLocation(prog, 'u_color4');
        const uI = gl.getUniformLocation(prog, 'u_intensity');

        const lerpFactor = 0.04;
        const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        function frame(now) {
            if (!Vibe3D.auroraGl) return;
            const elapsed = (now - Vibe3D.auroraStartTime) * (prefersReducedMotion ? 0.0002 : 0.001);

            for (let i = 0; i < 3; i++) {
                Vibe3D.auroraCurrentColors.color1[i] += (Vibe3D.auroraTargetColors.color1[i] - Vibe3D.auroraCurrentColors.color1[i]) * lerpFactor;
                Vibe3D.auroraCurrentColors.color2[i] += (Vibe3D.auroraTargetColors.color2[i] - Vibe3D.auroraCurrentColors.color2[i]) * lerpFactor;
                Vibe3D.auroraCurrentColors.color3[i] += (Vibe3D.auroraTargetColors.color3[i] - Vibe3D.auroraCurrentColors.color3[i]) * lerpFactor;
                Vibe3D.auroraCurrentColors.color4[i] += (Vibe3D.auroraTargetColors.color4[i] - Vibe3D.auroraCurrentColors.color4[i]) * lerpFactor;
            }

            gl.useProgram(prog);
            gl.uniform1f(uTime, elapsed);
            gl.uniform2f(uRes, canvas.width, canvas.height);
            gl.uniform3fv(uC1, Vibe3D.auroraCurrentColors.color1);
            gl.uniform3fv(uC2, Vibe3D.auroraCurrentColors.color2);
            gl.uniform3fv(uC3, Vibe3D.auroraCurrentColors.color3);
            gl.uniform3fv(uC4, Vibe3D.auroraCurrentColors.color4);
            gl.uniform1f(uI, prefersReducedMotion ? 0.55 : 1.0);
            gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
            requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    };

    Vibe3D.setAuroraState = function (state) {
        Vibe3D.auroraState = state;
        if (state === 'idle') {
            Vibe3D.auroraTargetColors = {
                color1: [0.05, 0.02, 0.15],
                color2: [0.0, 0.45, 0.55],
                color3: [0.4, 0.05, 0.5],
                color4: [0.0, 0.9, 0.7]
            };
        } else if (state === 'analyzing') {
            Vibe3D.auroraTargetColors = {
                color1: [0.10, 0.00, 0.30],
                color2: [0.0, 0.30, 0.85],
                color3: [0.85, 0.10, 0.65],
                color4: [0.0, 0.95, 0.85]
            };
        } else if (state === 'complete') {
            Vibe3D.auroraTargetColors = {
                color1: [0.10, 0.05, 0.0],
                color2: [0.95, 0.40, 0.0],
                color3: [0.95, 0.85, 0.10],
                color4: [0.10, 1.00, 0.40]
            };
        }
    };

    /* =========================================================================
       2. AUDIO-REACTIVE 3D PARTICLE MESH / VOXEL CORE
       ========================================================================= */

    Vibe3D.initAudioVisualizer = function (canvasId, mediaElementId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        Vibe3D.audio.canvas = canvas;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        function attachAudio() {
            if (Vibe3D.audio.ctx) return;
            const elem = document.getElementById(mediaElementId) || document.querySelector('video, audio');
            if (!elem) return;

            try {
                const AudioContext = window.AudioContext || window.webkitAudioContext;
                const actx = new AudioContext();
                const analyser = actx.createAnalyser();
                analyser.fftSize = 128;
                analyser.smoothingTimeConstant = 0.85;

                const source = actx.createMediaElementSource(elem);
                source.connect(analyser);
                analyser.connect(actx.destination);

                Vibe3D.audio.ctx = actx;
                Vibe3D.audio.analyser = analyser;
                Vibe3D.audio.sourceNode = source;
                Vibe3D.audio.dataArray = new Uint8Array(analyser.frequencyBinCount);
            } catch (err) {
                console.log('Web Audio element capture note:', err.message);
            }
        }

        window.addEventListener('click', attachAudio, { once: true });
        window.addEventListener('play', attachAudio, { capture: true, once: true });

        const particleCount = 72;
        const ringParticles = [];
        for (let i = 0; i < particleCount; i++) {
            ringParticles.push({
                angle: (i / particleCount) * Math.PI * 2,
                baseRadius: 65,
                currentRadius: 65,
                hue: (i / particleCount) * 360
            });
        }

        function renderVisualizer(now) {
            const dpr = window.devicePixelRatio || 1;
            const w = canvas.clientWidth;
            const h = canvas.clientHeight;
            if (canvas.width !== w * dpr || canvas.height !== h * dpr) {
                canvas.width = w * dpr;
                canvas.height = h * dpr;
                ctx.scale(dpr, dpr);
            }

            ctx.clearRect(0, 0, w, h);

            let bass = 0, mid = 0, treble = 0;
            if (Vibe3D.audio.analyser && Vibe3D.audio.dataArray) {
                Vibe3D.audio.analyser.getByteFrequencyData(Vibe3D.audio.dataArray);
                const data = Vibe3D.audio.dataArray;
                for (let i = 0; i < 8; i++) bass += data[i];
                for (let i = 8; i < 32; i++) mid += data[i];
                for (let i = 32; i < 64; i++) treble += data[i];
                bass /= (8 * 255);
                mid /= (24 * 255);
                treble /= (32 * 255);
            } else {
                const t = now * 0.002;
                bass = 0.2 + 0.15 * Math.sin(t * 1.5);
                mid = 0.15 + 0.1 * Math.cos(t * 2.0);
                treble = 0.1 + 0.08 * Math.sin(t * 3.0);
            }

            Vibe3D.audio.bassLevel = bass;
            Vibe3D.audio.midLevel = mid;
            Vibe3D.audio.trebleLevel = treble;

            const cx = w * 0.5;
            const cy = h * 0.5;
            const baseRad = Math.min(w, h) * 0.28 + bass * 25;

            ctx.save();
            ctx.translate(cx, cy);

            const coreGrad = ctx.createRadialGradient(0, 0, 5, 0, 0, baseRad * 0.8);
            coreGrad.addColorStop(0, `rgba(0, 255, 220, ${0.4 + bass * 0.5})`);
            coreGrad.addColorStop(0.6, `rgba(255, 0, 180, ${0.2 + mid * 0.4})`);
            coreGrad.addColorStop(1, 'rgba(0, 0, 0, 0)');
            ctx.beginPath();
            ctx.arc(0, 0, baseRad * 0.8, 0, Math.PI * 2);
            ctx.fillStyle = coreGrad;
            ctx.fill();

            ctx.beginPath();
            for (let i = 0; i < particleCount; i++) {
                const p = ringParticles[i];
                const freqOffset = (Vibe3D.audio.dataArray && Vibe3D.audio.dataArray[i % Vibe3D.audio.dataArray.length])
                    ? (Vibe3D.audio.dataArray[i % Vibe3D.audio.dataArray.length] / 255) * 45
                    : Math.sin(p.angle * 4 + now * 0.005) * 12 * (1 + bass);

                p.currentRadius += (baseRad + freqOffset - p.currentRadius) * 0.2;
                const px = Math.cos(p.angle) * p.currentRadius;
                const py = Math.sin(p.angle) * p.currentRadius;

                if (i === 0) ctx.moveTo(px, py);
                else ctx.lineTo(px, py);
            }
            ctx.closePath();
            ctx.lineWidth = 3 + bass * 4;
            ctx.strokeStyle = `hsl(${(now * 0.05) % 360}, 100%, 65%)`;
            ctx.shadowColor = '#00ffdc';
            ctx.shadowBlur = 15;
            ctx.stroke();

            for (let i = 0; i < particleCount; i += 3) {
                const p = ringParticles[i];
                const px = Math.cos(p.angle) * (p.currentRadius + 6);
                const py = Math.sin(p.angle) * (p.currentRadius + 6);

                ctx.beginPath();
                ctx.arc(px, py, 2.5 + bass * 3, 0, Math.PI * 2);
                ctx.fillStyle = '#ffffff';
                ctx.shadowColor = '#00ffdc';
                ctx.shadowBlur = 10;
                ctx.fill();
            }

            ctx.restore();
            requestAnimationFrame(renderVisualizer);
        }
        requestAnimationFrame(renderVisualizer);
    };

    /* =========================================================================
       3. CELEBRATION BURST — used by Reveal.razor when render completes.
       ========================================================================= */

    Vibe3D.triggerCelebrationBurst = function (x, y) {
        const canvas = Vibe3D.fx.canvas;
        if (!canvas) {
            const dyn = document.createElement('canvas');
            dyn.style.cssText = 'position:fixed;top:0;left:0;width:100vw;height:100vh;pointer-events:none;z-index:2000';
            document.body.appendChild(dyn);
            Vibe3D.fx.canvas = dyn;
            Vibe3D.fx.ctx = dyn.getContext('2d');
            function resize() {
                const dpr = window.devicePixelRatio || 1;
                dyn.width = window.innerWidth * dpr;
                dyn.height = window.innerHeight * dpr;
                dyn.style.width = window.innerWidth + 'px';
                dyn.style.height = window.innerHeight + 'px';
                Vibe3D.fx.ctx.scale(dpr, dpr);
            }
            window.addEventListener('resize', resize);
            resize();
            if (!Vibe3D.fx.animFrame) startFxLoop();
        }

        const ctx = Vibe3D.fx.ctx;
        if (!ctx) return;
        const originX = x !== undefined ? x : window.innerWidth * 0.5;
        const originY = y !== undefined ? y : window.innerHeight * 0.4;

        Vibe3D.fx.shockwaves.push({
            x: originX, y: originY, radius: 10, speed: 600, thickness: 8,
            alpha: 1.0, decay: 1.4, r: 0, g: 255, b: 220
        });
        Vibe3D.fx.shockwaves.push({
            x: originX, y: originY, radius: 5, speed: 400, thickness: 12,
            alpha: 0.8, decay: 1.0, r: 255, g: 0, b: 180
        });

        const colors = ['#00ffdc', '#ff007f', '#ffe600', '#00e5ff', '#ffffff', '#bf00ff', '#ff3d00'];
        for (let i = 0; i < 120; i++) {
            const angle = Math.random() * Math.PI * 2;
            const speed = 250 + Math.random() * 650;
            Vibe3D.fx.particles.push({
                x: originX, y: originY,
                vx: Math.cos(angle) * speed,
                vy: Math.sin(angle) * speed - 200,
                size: 8 + Math.random() * 8,
                color: colors[Math.floor(Math.random() * colors.length)],
                rotX: Math.random() * Math.PI,
                rotY: Math.random() * Math.PI,
                rotSpeedX: (Math.random() - 0.5) * 12,
                rotSpeedY: (Math.random() - 0.5) * 14,
                life: 3.5 + Math.random() * 1.5
            });
        }
    };

    function startFxLoop() {
        let lastTime = performance.now();
        function updateFx(now) {
            const dt = Math.min((now - lastTime) / 1000, 0.05);
            lastTime = now;
            const ctx = Vibe3D.fx.ctx;
            if (ctx && (Vibe3D.fx.particles.length > 0 || Vibe3D.fx.shockwaves.length > 0)) {
                ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);

                for (let i = Vibe3D.fx.shockwaves.length - 1; i >= 0; i--) {
                    const sw = Vibe3D.fx.shockwaves[i];
                    sw.radius += sw.speed * dt;
                    sw.alpha -= sw.decay * dt;
                    if (sw.alpha <= 0) { Vibe3D.fx.shockwaves.splice(i, 1); continue; }

                    ctx.beginPath();
                    ctx.arc(sw.x, sw.y, sw.radius, 0, Math.PI * 2);
                    ctx.lineWidth = sw.thickness * sw.alpha;
                    ctx.strokeStyle = `rgba(${sw.r}, ${sw.g}, ${sw.b}, ${sw.alpha})`;
                    ctx.shadowColor = `rgba(${sw.r}, ${sw.g}, ${sw.b}, 0.8)`;
                    ctx.shadowBlur = 20;
                    ctx.stroke();
                }

                for (let i = Vibe3D.fx.particles.length - 1; i >= 0; i--) {
                    const p = Vibe3D.fx.particles[i];
                    p.vy += 800 * dt;
                    p.vx *= 0.99;
                    p.x += p.vx * dt;
                    p.y += p.vy * dt;
                    p.rotX += p.rotSpeedX * dt;
                    p.rotY += p.rotSpeedY * dt;
                    p.life -= dt;
                    if (p.life <= 0 || p.y > window.innerHeight + 50) { Vibe3D.fx.particles.splice(i, 1); continue; }

                    ctx.save();
                    ctx.translate(p.x, p.y);
                    ctx.rotate(p.rotX);
                    ctx.scale(1, Math.cos(p.rotY));
                    ctx.fillStyle = p.color;
                    ctx.shadowColor = p.color;
                    ctx.shadowBlur = 6;
                    ctx.fillRect(-p.size * 0.5, -p.size * 0.5, p.size, p.size * 1.5);
                    ctx.restore();
                }
            }
            Vibe3D.fx.animFrame = requestAnimationFrame(updateFx);
        }
        Vibe3D.fx.animFrame = requestAnimationFrame(updateFx);
    }

    window.Vibe3D = Vibe3D;
    Vibe3D.initialized = true;

})();
