// GoF: Entity
namespace PoMemeVideo.Api.Features.MemeLibrary;

public class SoundAsset
{
    public Guid SoundId { get; init; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public int DurationMs { get; set; }
    public string[] ActionVectorTags { get; set; } = [];
    public required string BlobUrl { get; set; }
    public float[] EmbeddingVector { get; set; } = [];

    /// <summary>Curated wojak-storytelling staples the director should favor over generic matches.</summary>
    public bool Priority { get; set; }
}
