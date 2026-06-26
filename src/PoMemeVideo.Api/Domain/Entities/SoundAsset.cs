// GoF: Entity
namespace PoMemeVideo.Domain.Entities;

public class SoundAsset
{
    public Guid SoundId { get; init; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public int DurationMs { get; set; }
    public string[] ActionVectorTags { get; set; } = [];
    public required string BlobUrl { get; set; }
    public float[] EmbeddingVector { get; set; } = [];
}
