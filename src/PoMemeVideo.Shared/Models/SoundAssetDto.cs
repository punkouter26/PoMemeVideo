namespace PoMemeVideo.Shared.Models;

public class SoundAssetDto
{
    public Guid SoundId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public int DurationMs { get; init; }
    public string[] ActionVectorTags { get; init; } = [];
    public string BlobUrl { get; init; } = string.Empty;
}
