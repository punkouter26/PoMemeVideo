/**
 * keyframe-hologram.js
 *
 * 3D Holographic Parallax Tilt & Camera Shutter Audio for DitheredKeyframeStrip.
 * Implements mouse-driven 3D perspective rotation, scanline projection glow,
 * and synthesized camera shutter sound feedback.
 */
(function (global) {
    "use strict";

    class KeyframeHologram {
        attach(containerSelector = ".keyframe-strip") {
            const container = document.querySelector(containerSelector);
            if (!container) return;

            const cards = container.querySelectorAll(".keyframe-thumb");
            cards.forEach((card) => {
                if (card.__hologramAttached) return;
                card.__hologramAttached = true;

                card.style.transition = "transform 0.12s ease-out, box-shadow 0.12s ease-out";
                card.style.transformStyle = "preserve-3d";

                card.addEventListener("mouseenter", () => {
                    if (global.cyberAudio) {
                        global.cyberAudio.playCameraShutter();
                    }
                });

                card.addEventListener("mousemove", (e) => {
                    const rect = card.getBoundingClientRect();
                    const x = e.clientX - rect.left;
                    const y = e.clientY - rect.top;
                    const cx = rect.width * 0.5;
                    const cy = rect.height * 0.5;

                    const rotY = ((x - cx) / cx) * 18; // -18 to +18 deg
                    const rotX = -((y - cy) / cy) * 18;

                    card.style.transform = `perspective(600px) rotateX(${rotX.toFixed(1)}deg) rotateY(${rotY.toFixed(1)}deg) scale3d(1.1, 1.1, 1.1)`;
                    card.style.boxShadow = "0 8px 24px rgba(0, 255, 65, 0.5), inset 0 0 12px rgba(0, 255, 220, 0.4)";
                    card.style.borderColor = "#00ffdc";
                });

                card.addEventListener("mouseleave", () => {
                    card.style.transform = "perspective(600px) rotateX(0deg) rotateY(0deg) scale3d(1, 1, 1)";
                    card.style.boxShadow = "none";
                    card.style.borderColor = "#00FF41";
                });
            });
        }
    }

    global.keyframeHologram = new KeyframeHologram();
})(window);

