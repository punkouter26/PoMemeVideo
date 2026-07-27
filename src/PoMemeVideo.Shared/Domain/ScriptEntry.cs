// GoF: Entity
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Domain;

public class ScriptEntry
{
    public EntryId EntryId { get; init; } = EntryId.New();
    public required SessionId SessionId { get; init; }
    public long TimestampMs { get; set; }
    public required SoundId SoundId { get; set; }
    public string[] ActionVectorTags { get; set; } = [];
    /// <summary>Human-readable description of what is happening in the scene at this moment.</summary>
    public string SceneDescription { get; set; } = string.Empty;
    /// <summary>Display name of the meme sound chosen for this entry.</summary>
    public string SoundName { get; set; } = string.Empty;
    public string SelectionRationale { get; set; } = string.Empty;
    public bool IsIronic { get; set; }
    public VisualEffectType? VisualEffect { get; set; }
    public double? EffectIntensity { get; set; }
    public string? OverlayAssetId { get; set; }
    public PlacementType PlacementType { get; set; }
}
