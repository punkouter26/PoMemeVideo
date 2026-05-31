// Shared director prompt builder and response parser used by all AI backends.
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Infrastructure;

internal static class DirectorPrompt
{
    public static string Build(string labelsJson, string soundsJson) =>
        "You are a meme video director. Given video action labels and available meme sounds, " +
        "create a DirectorScript that maps meme sounds to moments in the video.\n\n" +
        $"Action labels (JSON): {labelsJson}\n" +
        $"Available sounds (JSON): {soundsJson}\n\n" +
        "IMPORTANT: If a label is generic (e.g. 'opening scene', 'mid-video action', 'final moments', 'unknown'), " +
        "choose a sound based purely on comedic timing and energy. Still write a creative sceneDescription " +
        "imagining what might be happening, and explain the sound choice with a specific rationale (tone, irony, cultural reference). " +
        "Always return an entry for every label provided — never return an empty array.\n\n" +
        "Return ONLY a JSON array of script entries. Each entry MUST include:\n" +
        "- sceneDescription: a vivid 1-2 sentence description of what is happening in the scene at that moment\n" +
        "- selectionRationale: explain specifically why this meme sound fits the scene (tone, timing, irony, cultural reference)\n" +
        "- soundId: must match one of the provided soundId values exactly\n" +
        "- timestampMs: timestamp in milliseconds\n" +
        "- actionVectorTags: array of action tags\n" +
        "- isIronic: true if the sound choice is ironic/subversive\n" +
        "- visualEffect: one of None, DeepFry, SnapZoom, MotionBlur, Overlay\n" +
        "- effectIntensity: 0.0 to 1.0\n\n" +
        "Example format:\n" +
        "[{\"timestampMs\": 3000, \"soundId\": \"...\", \"actionVectorTags\": [\"explosion\"], " +
        "\"sceneDescription\": \"A character stumbles backward after being hit, arms flailing wildly.\", " +
        "\"selectionRationale\": \"The comedic impact sound perfectly punctuates the physical chaos.\", " +
        "\"isIronic\": false, \"visualEffect\": \"SnapZoom\", \"effectIntensity\": 0.8}]\n" +
        "No extra text outside the JSON array.";

    public static ScriptEntry[] ParseResponse(
        string rawText,
        Guid sessionId,
        JsonSerializerOptions jsonOpts,
        IReadOnlyList<SoundAsset>? topCandidates = null)
    {
        var json = rawText.Trim();
        if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
        if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
        json = json.Trim();

        var dtos = JsonSerializer.Deserialize<DirectorEntry[]>(json, jsonOpts) ?? [];
        if (dtos.Length == 0)
            return [];

        var byName = topCandidates?
                         .GroupBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .ToDictionary(g => g.Key, g => g.First().SoundId, StringComparer.OrdinalIgnoreCase)
                     ?? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var byId = topCandidates?
                       .GroupBy(s => s.SoundId.ToString(), StringComparer.OrdinalIgnoreCase)
                       .ToDictionary(g => g.Key, g => g.First().SoundId, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        Guid ResolveSound(string raw)
        {
            if (byId.TryGetValue(raw, out var byIdMatch)) return byIdMatch;
            if (byName.TryGetValue(raw, out var byNameMatch)) return byNameMatch;
            if (Guid.TryParse(raw, out var parsed)) return parsed;
            return topCandidates is { Count: > 0 } ? topCandidates[0].SoundId : Guid.Empty;
        }

        return dtos.Select(e => new ScriptEntry
        {
            EntryId = Guid.NewGuid(),
            SessionId = sessionId,
            TimestampMs = e.TimestampMs,
            SoundId = ResolveSound(e.SoundIdRaw),
            ActionVectorTags = e.ActionVectorTags,
            SceneDescription = e.SceneDescription,
            SelectionRationale = e.SelectionRationale,
            IsIronic = e.IsIronic,
            VisualEffect = e.VisualEffect,
            EffectIntensity = e.EffectIntensity,
            PlacementType = PlacementType.Triggered,
        }).ToArray();
    }

    private sealed class DirectorEntry
    {
        [JsonPropertyName("timestampMs")] public long TimestampMs { get; init; }
        [JsonPropertyName("soundId")] public string SoundIdRaw { get; init; } = string.Empty;
        [JsonPropertyName("actionVectorTags")] public string[] ActionVectorTags { get; init; } = [];
        [JsonPropertyName("sceneDescription")] public string SceneDescription { get; init; } = string.Empty;
        [JsonPropertyName("selectionRationale")] public string SelectionRationale { get; init; } = string.Empty;
        [JsonPropertyName("isIronic")] public bool IsIronic { get; init; }
        [JsonPropertyName("visualEffect")] public VisualEffectType? VisualEffect { get; init; }
        [JsonPropertyName("effectIntensity")] public double? EffectIntensity { get; init; }
    }
}
