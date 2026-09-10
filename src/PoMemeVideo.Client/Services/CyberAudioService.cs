using Microsoft.JSInterop;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Interop bridge for the procedural Web Audio synthesizer (cyber-audio.js).
/// Provides zero-asset programmatic SFX for UI clicks, sub-bass drops,
/// telemetry chirps, Doppler whooshes, and celebratory fanfares.
/// </summary>
public class CyberAudioService
{
    private readonly IJSRuntime _js;

    public CyberAudioService(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask PlayClickAsync(double pitch = 1.0)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playClick", pitch);
        }
        catch { }
    }

    public async ValueTask PlayToggleAsync(bool state)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playToggle", state);
        }
        catch { }
    }

    public async ValueTask PlaySliderTickAsync(double normalizedFreq = 0.5)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playSliderTick", normalizedFreq);
        }
        catch { }
    }

    public async ValueTask PlayTelemetryChirpAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playTelemetryChirp");
        }
        catch { }
    }

    public async ValueTask PlaySubBassDropAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playSubBassDrop");
        }
        catch { }
    }

    public async ValueTask PlayFanfareAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playFanfare");
        }
        catch { }
    }

    public async ValueTask PlayElectrostaticAsync(double intensity = 0.5)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playElectrostatic", intensity);
        }
        catch { }
    }

    public async ValueTask PlayDopplerAsync(double proximity = 0.5)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playDoppler", proximity);
        }
        catch { }
    }

    public async ValueTask PlayCameraShutterAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playCameraShutter");
        }
        catch { }
    }

    public async ValueTask PlayErrorAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playError");
        }
        catch { }
    }

    public async ValueTask PlayWarningAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.playWarning");
        }
        catch { }
    }

    public async ValueTask<bool> ToggleMuteAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("cyberAudio.toggleMute");
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> GetMutedAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("cyberAudio.getMuted");
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask SetMutedAsync(bool muted)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.setMuted", muted);
        }
        catch { }
    }

    public async ValueTask SetVolumeAsync(double volume)
    {
        try
        {
            await _js.InvokeVoidAsync("cyberAudio.setVolume", volume);
        }
        catch { }
    }
}

