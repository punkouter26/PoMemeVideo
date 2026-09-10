/**
 * cyber-ambience.js
 *
 * Generative Cyberpunk Ambient Soundscape & Dynamic Audio-Ducking.
 * Generates an evolving analog drone (dual detuned triangle oscillators + sweeping LFO filter)
 * with automatic ducking during video/sound playback.
 */
(function (global) {
    "use strict";

    class CyberAmbienceEngine {
        constructor() {
            this.ctx = null;
            this.masterGain = null;
            this.duckGain = null;
            this.filter = null;
            this.lfo = null;
            this.osc1 = null;
            this.osc2 = null;
            this.enabled = false;
            this.targetVolume = 0.08;

            try {
                const saved = localStorage.getItem("pomeme_ambience_enabled");
                if (saved === "true") this.enabled = true;
            } catch (e) {}

            this._setupMediaMonitoring();
        }

        _ensureContext() {
            if (!this.ctx) {
                const AudioCtx = window.AudioContext || window.webkitAudioContext;
                if (!AudioCtx) return null;
                this.ctx = new AudioCtx();

                this.masterGain = this.ctx.createGain();
                this.masterGain.gain.setValueAtTime(this.enabled ? this.targetVolume : 0, this.ctx.currentTime);

                this.duckGain = this.ctx.createGain();
                this.duckGain.gain.setValueAtTime(1.0, this.ctx.currentTime);

                // Sweeping lowpass filter
                this.filter = this.ctx.createBiquadFilter();
                this.filter.type = "lowpass";
                this.filter.frequency.setValueAtTime(220, this.ctx.currentTime);
                this.filter.Q.setValueAtTime(4.0, this.ctx.currentTime);

                // LFO to sweep filter cutoff slowly (0.08 Hz)
                this.lfo = this.ctx.createOscillator();
                this.lfo.frequency.setValueAtTime(0.08, this.ctx.currentTime);

                const lfoGain = this.ctx.createGain();
                lfoGain.gain.setValueAtTime(120, this.ctx.currentTime);

                this.lfo.connect(lfoGain);
                lfoGain.connect(this.filter.frequency);

                // Dual detuned warm triangle oscillators (55Hz / 55.35Hz - note A1)
                this.osc1 = this.ctx.createOscillator();
                this.osc1.type = "triangle";
                this.osc1.frequency.setValueAtTime(55.0, this.ctx.currentTime);

                this.osc2 = this.ctx.createOscillator();
                this.osc2.type = "sine";
                this.osc2.frequency.setValueAtTime(55.35, this.ctx.currentTime);

                this.osc1.connect(this.filter);
                this.osc2.connect(this.filter);

                this.filter.connect(this.duckGain);
                this.duckGain.connect(this.masterGain);
                this.masterGain.connect(this.ctx.destination);

                this.osc1.start();
                this.osc2.start();
                this.lfo.start();
            }

            if (this.ctx.state === "suspended" && this.enabled) {
                this.ctx.resume().catch(() => {});
            }

            return this.ctx;
        }

        _setupMediaMonitoring() {
            // Monitor video / audio playback to duck ambience volume automatically
            setInterval(() => {
                if (!this.enabled || !this.duckGain || !this.ctx) return;

                let mediaPlaying = false;
                const mediaElements = document.querySelectorAll("video, audio");
                mediaElements.forEach((m) => {
                    if (!m.paused && m.currentTime > 0 && !m.muted) {
                        mediaPlaying = true;
                    }
                });

                const now = this.ctx.currentTime;
                const targetDuck = mediaPlaying ? 0.12 : 1.0;
                this.duckGain.gain.cancelScheduledValues(now);
                this.duckGain.gain.setTargetAtTime(targetDuck, now, 0.2);
            }, 250);
        }

        toggle() {
            return this.setEnabled(!this.enabled);
        }

        setEnabled(enable) {
            this.enabled = !!enable;
            try {
                localStorage.setItem("pomeme_ambience_enabled", this.enabled ? "true" : "false");
            } catch (e) {}

            this._ensureContext();
            if (this.masterGain && this.ctx) {
                const now = this.ctx.currentTime;
                this.masterGain.gain.cancelScheduledValues(now);
                this.masterGain.gain.setTargetAtTime(this.enabled ? this.targetVolume : 0, now, 0.4);
            }

            if (global.cyberAudio) {
                global.cyberAudio.playToggle(this.enabled);
            }

            return this.enabled;
        }

        isEnabled() {
            return this.enabled;
        }
    }

    global.cyberAmbience = new CyberAmbienceEngine();
})(window);

