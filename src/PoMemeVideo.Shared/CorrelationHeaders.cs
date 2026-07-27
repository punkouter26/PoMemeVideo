namespace PoMemeVideo.Shared;

/// <summary>
/// Header names carried across every hop (browser → BFF → downstream HTTP) so a single user
/// action can be stitched back together in logs and traces.
/// </summary>
public static class CorrelationHeaders
{
    public const string SessionId = "X-Session-ID";
    public const string CorrelationId = "X-Correlation-ID";

    /// <summary>Cookie the BFF issues to keep a session id stable across browser requests.</summary>
    public const string SessionCookieName = "pmv-session-id";
}
