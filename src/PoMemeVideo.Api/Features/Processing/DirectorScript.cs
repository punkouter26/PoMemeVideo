// GoF: Entity
namespace PoMemeVideo.Api.Entities;

public class DirectorScript
{
    public Guid SessionId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public int TotalSoundCount { get; set; }
    public double AverageDensitySeconds { get; set; }
    public string EntriesJson { get; set; } = "[]";
}
