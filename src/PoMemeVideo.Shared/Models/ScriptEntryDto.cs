using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Models;

public class ScriptEntryDto
{
    public Guid EntryId { get; init; }
    public Guid SessionId { get; init; }
    public long TimestampMs { get; init; }
    public Guid SoundId { get; init; }
    public string[] ActionVectorTags { get; init; } = [];
    /// <summary>Human-readable description of what is happening in the scene at this moment.</summary>
    public string SceneDescription { get; init; } = string.Empty;
    /// <summary>Display name of the meme sound chosen for this entry.</summary>
    public string SoundName { get; init; } = string.Empty;
    public string SelectionRationale { get; init; } = string.Empty;
    public bool IsIronic { get; init; }
    public VisualEffectType? VisualEffect { get; init; }
    public double? EffectIntensity { get; init; }
    public string? OverlayAssetId { get; init; }
    public PlacementType PlacementType { get; init; }
}
