import { playGlitchTransition } from './glitch-transition.js';

export async function playTransition() {
    await playGlitchTransition();
}

export function navigateTo(url) {
    window.location.href = url;
}

export function setupDualVideoSync(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (!v1 || !v2) return;

    let syncing = false;
    v1.addEventListener('play', () => {
        if (syncing) return;
        syncing = true;
        v2.currentTime = v1.currentTime;
        v2.play().catch(() => {});
        syncing = false;
    });
    v1.addEventListener('pause', () => {
        if (syncing) return;
        syncing = true;
        v2.pause();
        syncing = false;
    });
    v1.addEventListener('seeking', () => {
        if (syncing) return;
        syncing = true;
        v2.currentTime = v1.currentTime;
        syncing = false;
    });

    v2.addEventListener('play', () => {
        if (syncing) return;
        syncing = true;
        v1.currentTime = v2.currentTime;
        v1.play().catch(() => {});
        syncing = false;
    });
    v2.addEventListener('pause', () => {
        if (syncing) return;
        syncing = true;
        v1.pause();
        syncing = false;
    });
    v2.addEventListener('seeking', () => {
        if (syncing) return;
        syncing = true;
        v1.currentTime = v2.currentTime;
        syncing = false;
    });
}

export function masterPlay(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        v2.currentTime = v1.currentTime;
        v1.play().catch(() => {});
        v2.play().catch(() => {});
    }
}

export function masterPause(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        v1.pause();
        v2.pause();
    }
}

export function masterSeek(sourceId, memeId, timeSec) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        v1.currentTime = timeSec;
        v2.currentTime = timeSec;
    }
}
