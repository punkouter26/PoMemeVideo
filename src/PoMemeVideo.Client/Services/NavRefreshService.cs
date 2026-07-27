namespace PoMemeVideo.Client.Services;

/// <summary>
/// Allows pages to signal the NavBar to immediately refresh its AI provider badge
/// without waiting for the 30-second polling interval.
/// </summary>
public sealed class NavRefreshService
{
    private Func<Task>? _handler;

    public void Subscribe(Func<Task> handler) => _handler = handler;
    public void Unsubscribe() => _handler = null;

    public async Task NotifyAiChangedAsync()
    {
        if (_handler is not null)
            await _handler();
    }
}
