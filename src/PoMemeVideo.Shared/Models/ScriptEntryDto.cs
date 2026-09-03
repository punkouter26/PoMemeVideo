using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Models;

public class ScriptEntryDto
{
    public Guid EntryId { get; set; }
    public Guid SessionId { get; set; }
    public long TimestampMs { get; set; }
    public Guid SoundId { get; set; }
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
    public string? CaptionText { get; set; }
    public string? CaptionPosition { get; set; }
}
