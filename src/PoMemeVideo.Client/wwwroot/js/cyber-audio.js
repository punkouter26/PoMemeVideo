/**
 * cyber-audio.js
 *
 * Procedural Web Audio API sound synthesizer and cyber-haptic engine.
 * Generates all UI micro-interaction SFX, sub-bass drops, telemetry chirps,
 * Doppler whooshes, and fanfare programmatically with zero MP3/WAV dependencies.
 */
(function (global) {
    "use strict";

    class CyberAudioEngine {
        constructor() {
            this.ctx = null;
            this.masterGain = null;
            this.analyser = null;
            this.isMuted = false;
            this.masterVolume = 0.65;
            this.initialized = false;
            this.unlocked = false;

            // Load saved preferences
            try {
                const savedMute = localStorage.getItem("pomeme_sfx_muted");
                if (savedMute !== null) this.isMuted = savedMute === "true";
                const savedVol = localStorage.getItem("pomeme_sfx_volume");
                if (savedVol !== null) this.masterVolume = parseFloat(savedVol) || 0.65;
            } catch (e) {}

            this._setupAutoUnlock();
            this._setupEventDelegation();
        }

        _ensureContext() {
            if (!this.ctx) {
                const AudioCtx = window.AudioContext || window.webkitAudioContext;
                if (!AudioCtx) return null;
                this.ctx = new AudioCtx();

                this.masterGain = this.ctx.createGain();
                this.masterGain.gain.setValueAtTime(this.isMuted ? 0 : this.masterVolume, this.ctx.currentTime);

                this.analyser = this.ctx.createAnalyser();
                this.analyser.fftSize = 256;
                this.analyser.smoothingTimeConstant = 0.8;

                this.masterGain.connect(this.analyser);
                this.analyser.connect(this.ctx.destination);
                this.initialized = true;
            }

            if (this.ctx.state === "suspended") {
                this.ctx.resume().catch(() => {});
            }

            return this.ctx;
        }

        _setupAutoUnlock() {
            const unlock = () => {
                if (this.unlocked) return;
                const ctx = this._ensureContext();
                if (ctx && ctx.state === "running") {
                    this.unlocked = true;
                    window.removeEventListener("click", unlock);
                    window.removeEventListener("keydown", unlock);
                    window.removeEventListener("touchstart", unlock);
                }
            };

            window.addEventListener("click", unlock, { passive: true });
            window.addEventListener("keydown", unlock, { passive: true });
            window.addEventListener("touchstart", unlock, { passive: true });
        }

        _setupEventDelegation() {
            window.addEventListener("click", (e) => {
                if (this.isMuted) return;
                const target = e.target;
                if (!target || !(target instanceof HTMLElement)) return;

                // Check for opt-out
                if (target.closest('[data-cyber-sound="none"]')) return;

                const customSound = target.closest("[data-cyber-sound]");
                if (customSound) {
                    const soundType = customSound.getAttribute("data-cyber-sound");
                    if (typeof this[soundType] === "function") {
                        this[soundType]();
                        return;
                    }
                }

                // Check for toggle inputs
                if (target.matches('input[type="checkbox"]')) {
                    this.playToggle(target.checked);
                    return;
                }

                // Check for buttons, tabs, select, navigation links
                if (target.closest('button, .ascii-btn, .step-pill, .nav-links a, .mobile-nav-link, .cursor-pointer')) {
                    this.playClick();
                }
            }, true);
        }

        setMuted(muted) {
            this.isMuted = !!muted;
            try {
                localStorage.setItem("pomeme_sfx_muted", this.isMuted ? "true" : "false");
            } catch (e) {}

            if (this.masterGain && this.ctx) {
                const now = this.ctx.currentTime;
                this.masterGain.gain.cancelScheduledValues(now);
                this.masterGain.gain.setValueAtTime(this.masterGain.gain.value, now);
                this.masterGain.gain.linearRampToValueAtTime(this.isMuted ? 0 : this.masterVolume, now + 0.05);
            }
            return this.isMuted;
        }

        toggleMute() {
            return this.setMuted(!this.isMuted);
        }

        getMuted() {
            return this.isMuted;
        }

        setVolume(vol) {
            this.masterVolume = Math.max(0, Math.min(1, vol));
            try {
                localStorage.setItem("pomeme_sfx_volume", this.masterVolume.toString());
            } catch (e) {}

            if (this.masterGain && this.ctx && !this.isMuted) {
                this.masterGain.gain.setValueAtTime(this.masterVolume, this.ctx.currentTime);
            }
        }

        getVolume() {
            return this.masterVolume;
        }

        // ── Procedural SFX Generators ──────────────────────────────────────

        /**
         * Mechanical relay click (noise burst + 1.2kHz transient)
         */
        playClick(pitch = 1.0) {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;

            // 1. Noise transient (15ms bandpass burst)
            const bufferSize = Math.floor(ctx.sampleRate * 0.015);
            const noiseBuffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate);
            const output = noiseBuffer.getChannelData(0);
            for (let i = 0; i < bufferSize; i++) {
                output[i] = Math.random() * 2 - 1;
            }

            const noiseNode = ctx.createBufferSource();
            noiseNode.buffer = noiseBuffer;

            const bandpass = ctx.createBiquadFilter();
            bandpass.type = "bandpass";
            bandpass.frequency.setValueAtTime(1400 * pitch, now);
            bandpass.Q.setValueAtTime(3.5, now);

            const noiseGain = ctx.createGain();
            noiseGain.gain.setValueAtTime(0.35, now);
            noiseGain.gain.exponentialRampToValueAtTime(0.001, now + 0.015);

            noiseNode.connect(bandpass);
            bandpass.connect(noiseGain);
            noiseGain.connect(this.masterGain);

            noiseNode.start(now);
            noiseNode.stop(now + 0.016);

            // 2. High-frequency pulse transient
            const osc = ctx.createOscillator();
            const oscGain = ctx.createGain();

            osc.type = "sine";
            osc.frequency.setValueAtTime(950 * pitch, now);
            osc.frequency.exponentialRampToValueAtTime(300 * pitch, now + 0.012);

            oscGain.gain.setValueAtTime(0.25, now);
            oscGain.gain.exponentialRampToValueAtTime(0.001, now + 0.012);

            osc.connect(oscGain);
            oscGain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 0.013);
        }

        /**
         * Dual-frequency toggle glide
         */
        playToggle(state) {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();

            osc.type = "triangle";
            const startFreq = state ? 300 : 650;
            const endFreq = state ? 650 : 280;

            osc.frequency.setValueAtTime(startFreq, now);
            osc.frequency.exponentialRampToValueAtTime(endFreq, now + 0.06);

            gain.gain.setValueAtTime(0.28, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 0.07);

            osc.connect(gain);
            gain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 0.075);
        }

        /**
         * Granular slider tick for timeline and slider scrubbing
         */
        playSliderTick(normalizedFreq = 0.5) {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();

            const freq = 450 + normalizedFreq * 750; // 450Hz - 1200Hz
            osc.type = "sine";
            osc.frequency.setValueAtTime(freq, now);

            gain.gain.setValueAtTime(0.18, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 0.008);

            osc.connect(gain);
            gain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 0.009);
        }

        /**
         * FM synthesis sci-fi telemetry chirp for live AI stream updates
         */
        playTelemetryChirp() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;

            // Carrier
            const carrier = ctx.createOscillator();
            carrier.type = "sine";
            const carrierFreq = 880 + Math.random() * 440;
            carrier.frequency.setValueAtTime(carrierFreq, now);

            // Modulator
            const mod = ctx.createOscillator();
            mod.type = "triangle";
            mod.frequency.setValueAtTime(110, now);

            const modGain = ctx.createGain();
            modGain.gain.setValueAtTime(320, now);
            modGain.gain.exponentialRampToValueAtTime(1, now + 0.045);

            mod.connect(modGain);
            modGain.connect(carrier.frequency);

            const masterChirpGain = ctx.createGain();
            masterChirpGain.gain.setValueAtTime(0.22, now);
            masterChirpGain.gain.exponentialRampToValueAtTime(0.001, now + 0.045);

            carrier.connect(masterChirpGain);
            masterChirpGain.connect(this.masterGain);

            mod.start(now);
            carrier.start(now);
            mod.stop(now + 0.048);
            carrier.stop(now + 0.048);
        }

        /**
         * 808 Sub-Bass Drop with soft tanh waveshaping
         */
        playSubBassDrop() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();

            // Waveshaper for subtle warm harmonic distortion
            const shaper = ctx.createWaveShaper();
            const n_samples = 256;
            const curve = new Float32Array(n_samples);
            for (let i = 0; i < n_samples; ++i) {
                const x = (i * 2) / n_samples - 1;
                curve[i] = Math.tanh(x * 1.5);
            }
            shaper.curve = curve;

            osc.type = "sine";
            osc.frequency.setValueAtTime(175, now);
            osc.frequency.exponentialRampToValueAtTime(32, now + 1.1);

            gain.gain.setValueAtTime(0.85, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 1.2);

            osc.connect(shaper);
            shaper.connect(gain);
            gain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 1.25);
        }

        /**
         * Shimmering pentatonic fanfare for celebration
         */
        playFanfare() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const notes = [523.25, 659.25, 783.99, 987.77, 1046.50]; // C5, E5, G5, B5, C6
            notes.forEach((freq, idx) => {
                const now = ctx.currentTime + idx * 0.075;
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();

                osc.type = "triangle";
                osc.frequency.setValueAtTime(freq, now);

                gain.gain.setValueAtTime(0.24, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.35);

                osc.connect(gain);
                gain.connect(this.masterGain);

                osc.start(now);
                osc.stop(now + 0.38);
            });
        }

        /**
         * Electrostatic crackle for laser wipe drag
         */
        playElectrostatic(intensity = 0.5) {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const duration = 0.03 + intensity * 0.04;
            const bufferSize = Math.floor(ctx.sampleRate * duration);
            const noiseBuffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate);
            const output = noiseBuffer.getChannelData(0);

            for (let i = 0; i < bufferSize; i++) {
                output[i] = (Math.random() > 0.85 ? 1 : -1) * Math.random();
            }

            const noiseNode = ctx.createBufferSource();
            noiseNode.buffer = noiseBuffer;

            const highpass = ctx.createBiquadFilter();
            highpass.type = "highpass";
            highpass.frequency.setValueAtTime(2500, now);

            const gain = ctx.createGain();
            gain.gain.setValueAtTime(0.2 * intensity, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + duration);

            noiseNode.connect(highpass);
            highpass.connect(gain);
            gain.connect(this.masterGain);

            noiseNode.start(now);
            noiseNode.stop(now + duration);
        }

        /**
         * Doppler whoosh for dropzone hover
         */
        playDoppler(proximity = 0.5) {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const bufferSize = Math.floor(ctx.sampleRate * 0.12);
            const noiseBuffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate);
            const output = noiseBuffer.getChannelData(0);
            for (let i = 0; i < bufferSize; i++) {
                output[i] = Math.random() * 2 - 1;
            }

            const noiseNode = ctx.createBufferSource();
            noiseNode.buffer = noiseBuffer;

            const filter = ctx.createBiquadFilter();
            filter.type = "bandpass";
            const targetFreq = 300 + proximity * 2800;
            filter.frequency.setValueAtTime(targetFreq * 0.6, now);
            filter.frequency.exponentialRampToValueAtTime(targetFreq, now + 0.1);
            filter.Q.setValueAtTime(6.0, now);

            const gain = ctx.createGain();
            gain.gain.setValueAtTime(0.18 * proximity, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 0.12);

            noiseNode.connect(filter);
            filter.connect(gain);
            gain.connect(this.masterGain);

            noiseNode.start(now);
            noiseNode.stop(now + 0.13);
        }

        /**
         * Mechanical camera shutter click for keyframes
         */
        playCameraShutter() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;

            // Click 1 (open shutter)
            this.playClick(1.2);

            // Click 2 (close shutter 45ms later)
            setTimeout(() => {
                this.playClick(0.9);
            }, 45);
        }

        /**
         * Error dissonance buzz
         */
        playError() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const osc1 = ctx.createOscillator();
            const osc2 = ctx.createOscillator();
            const gain = ctx.createGain();

            osc1.type = "sawtooth";
            osc2.type = "sawtooth";
            osc1.frequency.setValueAtTime(145, now);
            osc2.frequency.setValueAtTime(153, now); // 8Hz beating dissonance

            gain.gain.setValueAtTime(0.3, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 0.28);

            osc1.connect(gain);
            osc2.connect(gain);
            gain.connect(this.masterGain);

            osc1.start(now);
            osc2.start(now);
            osc1.stop(now + 0.3);
            osc2.stop(now + 0.3);
        }

        /**
         * Caution warning chirp
         */
        playWarning() {
            const ctx = this._ensureContext();
            if (!ctx || this.isMuted) return;

            const now = ctx.currentTime;
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();

            osc.type = "square";
            osc.frequency.setValueAtTime(740, now);
            osc.frequency.setValueAtTime(587, now + 0.08);

            gain.gain.setValueAtTime(0.2, now);
            gain.gain.exponentialRampToValueAtTime(0.001, now + 0.18);

            osc.connect(gain);
            gain.connect(this.masterGain);

            osc.start(now);
            osc.stop(now + 0.2);
        }
    }

    global.cyberAudio = new CyberAudioEngine();
})(window);

