using Microsoft.JSInterop;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Thin wrapper over the small slice of <c>vibe3d-engine.js</c> the app still uses:
/// the background aurora animation and a one-shot celebration burst. The audio
/// visualizer, physics, FX, and parallax surfaces were removed — anything that
/// still calls them must be reworked or deleted.
/// </summary>
public class Vibe3DService
{
    private readonly IJSRuntime _js;

    public Vibe3DService(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask InitAuroraAsync(string canvasId)
    {
        try
        {
            await _js.InvokeVoidAsync("Vibe3D.initAurora", canvasId);
        }
        catch
        {
            // Non-critical — JS interop failures during pre-render or teardown are expected.
        }
    }

    public async ValueTask SetAuroraStateAsync(string state)
    {
        try
        {
            await _js.InvokeVoidAsync("Vibe3D.setAuroraState", state);
        }
        catch
        {
            // Non-critical — the aurora falls back to its idle palette when JS isn't ready.
        }
    }

    public async ValueTask TriggerCelebrationBurstAsync(double? x = null, double? y = null)
    {
        try
        {
            await _js.InvokeVoidAsync("Vibe3D.triggerCelebrationBurst", x, y);
        }
        catch
        {
            // Non-critical — the celebration is purely cosmetic.
        }
    }
}
