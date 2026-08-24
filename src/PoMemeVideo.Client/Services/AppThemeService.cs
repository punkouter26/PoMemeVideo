using Microsoft.JSInterop;

namespace PoMemeVideo.Client.Services;

public class AppThemeService
{
    private readonly IJSRuntime _js;

    public string CurrentTheme { get; private set; } = "cyber-emerald";

    public event Action? OnThemeChanged;

    public AppThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SetThemeAsync(string theme)
    {
        CurrentTheme = theme;
        try
        {
            await _js.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme', '{theme}')");
        }
        catch { }

        OnThemeChanged?.Invoke();
    }

    public async Task CycleThemeAsync()
    {
        var next = CurrentTheme switch
        {
            "cyber-emerald" => "high-contrast-amber",
            "high-contrast-amber" => "vaporwave-neon",
            "vaporwave-neon" => "oled-black",
            _ => "cyber-emerald"
        };
        await SetThemeAsync(next);
    }
}