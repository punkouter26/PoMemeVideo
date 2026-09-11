import { playGlitchTransition } from './glitch-transition.js';

export async function playTransition() {
    await playGlitchTransition();
}

export function navigateTo(url) {
    window.location.href = url;
}

// Mirroring a seek writes currentTime on the other element, which raises ITS `seeking` event on a
// later task — by then a synchronous guard flag has already been cleared, so the two elements bounce
// the seek back and forth indefinitely. Measured before this guard: ~30,000 `seeking` events per
// second with zero `seeked` events, both players pinned at the same timestamp and unable to play.
// A time-window guard survives the asynchronous dispatch; a synchronous boolean does not.
const MIRROR_GUARD_MS = 400;
const SYNC_TOLERANCE_SEC = 0.12;
let mirrorGuardUntil = 0;

const mirrorSuppressed = () => performance.now() < mirrorGuardUntil;
const holdMirrorGuard = () => { mirrorGuardUntil = performance.now() + MIRROR_GUARD_MS; };

export function setupDualVideoSync(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (!v1 || !v2) return;

    // Only re-seek when the players have genuinely drifted apart; unconditional writes are what
    // fed the feedback loop in the first place.
    const mirrorSeek = (from, to) => {
        if (mirrorSuppressed()) return;
        holdMirrorGuard();
        if (Math.abs(to.currentTime - from.currentTime) > SYNC_TOLERANCE_SEC) {
            to.currentTime = from.currentTime;
        }
    };
    const mirrorPlay = (from, to) => {
        if (mirrorSuppressed()) return;
        holdMirrorGuard();
        if (Math.abs(to.currentTime - from.currentTime) > SYNC_TOLERANCE_SEC) {
            to.currentTime = from.currentTime;
        }
        to.play().catch(() => {});
    };
    const mirrorPause = (to) => {
        if (mirrorSuppressed()) return;
        holdMirrorGuard();
        to.pause();
    };

    v1.addEventListener('play', () => mirrorPlay(v1, v2));
    v1.addEventListener('pause', () => mirrorPause(v2));
    v1.addEventListener('seeking', () => mirrorSeek(v1, v2));

    v2.addEventListener('play', () => mirrorPlay(v2, v1));
    v2.addEventListener('pause', () => mirrorPause(v1));
    v2.addEventListener('seeking', () => mirrorSeek(v2, v1));
}

export function masterPlay(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        holdMirrorGuard();
        v2.currentTime = v1.currentTime;
        v1.play().catch(() => {});
        v2.play().catch(() => {});
    }
}

export function masterPause(sourceId, memeId) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        holdMirrorGuard();
        v1.pause();
        v2.pause();
    }
}

export function masterSeek(sourceId, memeId, timeSec) {
    const v1 = document.getElementById(sourceId);
    const v2 = document.getElementById(memeId);
    if (v1 && v2) {
        holdMirrorGuard();
        v1.currentTime = timeSec;
        v2.currentTime = timeSec;
    }
}
