// Shared director prompt builder and response parser used by all AI backends.
using PoMemeVideo.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Processing;

internal static class DirectorPrompt
{
    /// <summary>
    /// Canonical JSON projection of the sound menu offered to the LLM. Shared by all
    /// director backends so the prompt guidance below always matches the fields sent.
    /// </summary>
    public static string SerializeSounds(IEnumerable<SoundAsset> sounds, JsonSerializerOptions jsonOpts)
        => JsonSerializer.Serialize(
            sounds.Select(s => new
            {
                s.SoundId,
                s.DisplayName,
                Tags = s.ActionVectorTags,
                UseCase = string.IsNullOrWhiteSpace(s.UseCase) ? null : s.UseCase,
                Priority = s.Priority ? true : (bool?)null,
            }),
            jsonOpts);

    public static string Build(string labelsJson, string soundsJson, bool hasRealVisionData = false)
    {
        var visionContext = hasRealVisionData
            ? "The labels come from real AI Vision analysis of the video frames — use them to describe what is actually happening on screen."
            : "NOTE: No visual frame analysis was available for this video. " +
              "The labels are TIME-BASED PLACEHOLDERS (e.g. 'opening scene', 'mid-video action', 'final moments') — " +
              "they describe timing position only, NOT actual visual content. " +
              "For sceneDescription write exactly: '[Time-based placement at {timestamp}]' and do NOT invent or imagine scene content. " +
              "Focus selectionRationale purely on why the sound fits the timing and energy of that video moment.";

        return "You are a meme video director. Given video action labels and available meme sounds, " +
               "create a DirectorScript that maps meme sounds to moments in the video.\n\n" +
               $"Action labels (JSON): {labelsJson}\n" +
               $"Available sounds (JSON): {soundsJson}\n\n" +
               $"IMPORTANT: {visionContext} " +
               "Always return an entry for every label provided — never return an empty array.\n\n" +
               "Choosing sounds: you may pick ANY sound from the list for any entry — the list is a menu, " +
               "not a per-label assignment. Use each sound's tags and useCase hint to judge fit. " +
               "Sounds marked \"priority\": true are curated wojak-storytelling classics (Low Budget Stories style) — " +
               "prefer them when several sounds fit a moment equally well, and avoid repeating the same sound twice.\n\n" +
               "Return ONLY a JSON array of script entries. Each entry MUST include:\n" +
               "- sceneDescription: see vision context above\n" +
               "- selectionRationale: explain specifically why this meme sound fits (tone, timing, irony, cultural reference)\n" +
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
    }

    public static ScriptEntry[] ParseResponse(
        string rawText,
        SessionId sessionId,
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
                     ?? new Dictionary<string, SoundId>(StringComparer.OrdinalIgnoreCase);
        var byId = topCandidates?
                       .GroupBy(s => s.SoundId.ToString(), StringComparer.OrdinalIgnoreCase)
                       .ToDictionary(g => g.Key, g => g.First().SoundId, StringComparer.OrdinalIgnoreCase)
                   ?? new Dictionary<string, SoundId>(StringComparer.OrdinalIgnoreCase);

        SoundId ResolveSound(string raw)
        {
            if (byId.TryGetValue(raw, out var byIdMatch)) return byIdMatch;
            if (byName.TryGetValue(raw, out var byNameMatch)) return byNameMatch;
            if (Guid.TryParse(raw, out var parsed)) return new SoundId(parsed);
            return topCandidates is { Count: > 0 } ? topCandidates[0].SoundId : SoundId.Empty;
        }

        return dtos.Select(e => new ScriptEntry
        {
            EntryId = EntryId.New(),
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
