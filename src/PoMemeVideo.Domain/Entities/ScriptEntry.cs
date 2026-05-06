// GoF: Entity
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Domain.Entities;

public class ScriptEntry
{
    public Guid EntryId { get; init; } = Guid.NewGuid();
    public required Guid SessionId { get; init; }
    public long TimestampMs { get; set; }
    public required Guid SoundId { get; set; }
    public string[] ActionVectorTags { get; set; } = [];
    public string SelectionRationale { get; set; } = string.Empty;
    public bool IsIronic { get; set; }
    public VisualEffectType? VisualEffect { get; set; }
    public double? EffectIntensity { get; set; }
    public string? OverlayAssetId { get; set; }
    public PlacementType PlacementType { get; set; }
}
