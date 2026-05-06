/**
 * glitch-transition.js (T066)
 * Plays a 1.2-second glitch/flicker transition effect.
 * 
 * Usage:
 *   import { playGlitchTransition } from './glitch-transition.js';
 *   await playGlitchTransition(() => console.log('done'));
 */

export async function playGlitchTransition(onComplete) {
    const body = document.body;
    const startTime = Date.now();
    const duration = 1200; // 1.2 seconds in ms
    
    // Create glitch overlay div if it doesn't exist
    let overlay = document.getElementById('glitch-overlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'glitch-overlay';
        overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: #000;
            color: #00FF41;
            font-family: 'Courier New', monospace;
            z-index: 9999;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 2rem;
            font-weight: bold;
            text-shadow: 0 0 10px #00FF41;
            overflow: hidden;
        `;
        body.appendChild(overlay);
    }
    
    // Apply glitch animation
    return new Promise((resolve) => {
        const animationFrame = setInterval(() => {
            const elapsed = Date.now() - startTime;
            const progress = Math.min(elapsed / duration, 1);
            
            // Rapid flicker effect
            if (Math.random() > 0.5) {
                overlay.style.opacity = '1';
                overlay.style.textContent = '[ ℂℝ𝕿 𝔾𝕳𝕺𝔼𝔽𝕿 ]';
                overlay.style.filter = `hue-rotate(${Math.random() * 360}deg) brightness(${0.5 + Math.random() * 0.5})`;
            } else {
                overlay.style.opacity = '0.7';
                overlay.textContent = '';
                overlay.style.filter = 'none';
            }
            
            // Screen flash lines
            if (progress > 0.3 && Math.random() > 0.7) {
                const lineHeight = Math.random() * 100;
                overlay.style.backgroundImage = `linear-gradient(0deg, transparent ${lineHeight}%, rgba(0, 255, 65, 0.1) ${lineHeight + 2}%)`;
            } else {
                overlay.style.backgroundImage = 'none';
            }
            
            // Check if transition is complete
            if (progress >= 1) {
                clearInterval(animationFrame);
                
                // Fade out overlay
                overlay.style.transition = 'opacity 0.5s ease-out';
                overlay.style.opacity = '0';
                
                setTimeout(() => {
                    overlay.remove();
                    if (onComplete) onComplete();
                    resolve();
                }, 500);
            }
        }, 50); // Update every 50ms for smooth flicker
    });
}
