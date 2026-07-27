import { playGlitchTransition } from './glitch-transition.js';

export async function playTransition() {
    await playGlitchTransition();
}

export function navigateTo(url) {
    window.location.href = url;
}
