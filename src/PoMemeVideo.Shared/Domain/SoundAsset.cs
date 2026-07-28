// GoF: Entity
namespace PoMemeVideo.Shared.Domain;

public class SoundAsset
{
    public SoundId SoundId { get; init; } = SoundId.New();
    public required string DisplayName { get; set; }
    public int DurationMs { get; set; }
    public string[] ActionVectorTags { get; set; } = [];
    public required string BlobUrl { get; set; }

    /// <summary>Curated wojak-storytelling staples the director should favor over generic matches.</summary>
    public bool Priority { get; set; }

    /// <summary>Human-readable hint for the AI director describing when this sound fits.</summary>
    public string UseCase { get; set; } = string.Empty;
}
