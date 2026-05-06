namespace PoMemeVideo.Api.Configuration;

public class FeatureFlags
{
    public const string SectionName = "FeatureFlags";

    public bool UseMockAI { get; init; }
}
