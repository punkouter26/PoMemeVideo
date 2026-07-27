// GoF: Entity
namespace PoMemeVideo.Shared.Domain;

public class DirectorScript
{
    public SessionId SessionId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public int TotalSoundCount { get; set; }
    public double AverageDensitySeconds { get; set; }
    public string EntriesJson { get; set; } = "[]";
}
