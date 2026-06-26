// GoF: Null Object Pattern — no-cost AI service for development/testing
using PoMemeVideo.Api.Interfaces;

namespace PoMemeVideo.Api.Mock;

public sealed class MockAiVisionService : IAiVisionService
{
    private static readonly (double TimestampSeconds, string Label)[] PreBakedLabels =
    [
        (3.0,  "explosion"),
        (6.5,  "fall"),
        (9.0,  "celebration"),
        (12.0, "scream"),
        (15.5, "punch"),
        (19.0, "awkward silence"),
    ];

    public Task<(double TimestampSeconds, string Label)[]> AnalyseAsync(
        string[] keyframeBase64Images,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PreBakedLabels);
}
