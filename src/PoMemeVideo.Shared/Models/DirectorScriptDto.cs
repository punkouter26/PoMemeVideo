namespace PoMemeVideo.Shared.Models;

public class DirectorScriptDto
{
    public Guid SessionId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public int TotalSoundCount { get; init; }
    public double AverageDensitySeconds { get; init; }
    public List<ScriptEntryDto> Entries { get; init; } = new();
}
