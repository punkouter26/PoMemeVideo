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

    /// <summary>
    /// Compact token representation of the candidate sound menu (65-75% token reduction).
    /// </summary>
    public static string SerializeSoundsCompact(IReadOnlyList<SoundAsset> sounds)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < sounds.Count; i++)
        {
            var s = sounds[i];
            sb.Append('[').Append(i).Append("] ").Append(s.DisplayName);
            if (s.ActionVectorTags.Length > 0)
                sb.Append(" | tags: ").Append(string.Join(",", s.ActionVectorTags));
            if (s.Priority)
                sb.Append(" | priority");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Build(string labelsJson, string soundsJson, bool hasRealVisionData = false, string? memePersona = null)
    {
        var visionContext = hasRealVisionData
            ? "The labels come from real AI Vision analysis of the video frames — use them to describe what is actually happening on screen."
            : "NOTE: No visual frame analysis was available for this video. " +
              "The labels are TIME-BASED PLACEHOLDERS (e.g. 'opening scene', 'mid-video action', 'final moments') — " +
              "they describe timing position only, NOT actual visual content. " +
              "For sceneDescription write exactly: '[Time-based placement at {timestamp}]' and do NOT invent or imagine scene content. " +
              "Focus selectionRationale purely on why the sound fits the timing and energy of that video moment.";

        var personaContext = (memePersona?.ToLowerInvariant()) switch
        {
            "brainrot" => "DIRECTOR PERSONA: Gen-Z / Brainrot. Use chaotic, fast-paced meme logic (vine boom, metal pipe, goofy ahh humor). Write punchy modern slang captions.",
            "mlg" => "DIRECTOR PERSONA: 2016 Classic MLG montage parody. Loud, hyper-ironic, airhorn & hitmarker gaming energy. Write uppercase gaming captions.",
            "sitcom" => "DIRECTOR PERSONA: 90s TV Sitcom & Comedy Club. Emphasize punchlines, awkward pauses, and laugh-track comedic timing.",
            "drama" => "DIRECTOR PERSONA: Over-dramatic cinematic thriller. Use heavy suspense, sudden dramatic cuts, and intense caption text.",
            "anime" => "DIRECTOR PERSONA: Over-the-top Anime / Shonen battle parody. High melodrama, 'Nani?!' moments, and freeze-frame tension.",
            _ => "DIRECTOR PERSONA: Modern internet meme humor. Punchy, witty, ironic, and well-timed."
        };

        return $"You are an expert meme video director. {personaContext}\n" +
               "Given video action labels and available meme sounds, create a DirectorScript that maps meme sounds and punchline captions to moments in the video.\n\n" +
               $"Action labels (JSON): {labelsJson}\n" +
               $"Available sounds: {soundsJson}\n\n" +
               $"IMPORTANT: {visionContext} " +
               "Always return an entry for every label provided — never return an empty array.\n\n" +
               "Choosing sounds: you may pick ANY sound from the list for any entry — the list is a menu, " +
               "not a per-label assignment. Use each sound's tags and useCase hint to judge fit. " +
               "Sounds marked priority are curated wojak-storytelling classics — " +
               "prefer them when several sounds fit a moment equally well, and avoid repeating the same sound twice.\n\n" +
               "Return ONLY a JSON array of script entries. Each entry MUST include:\n" +
               "- sceneDescription: see vision context above\n" +
               "- selectionRationale: explain specifically why this meme sound fits (tone, timing, irony, cultural reference)\n" +
               "- soundId or soundIndex: match the soundId or the numeric index [i] from the available sounds\n" +
               "- timestampMs: timestamp in milliseconds\n" +
               "- actionVectorTags: array of action tags\n" +
               "- isIronic: true if the sound choice is ironic/subversive\n" +
               "- visualEffect: one of None, DeepFry, SnapZoom, MotionBlur, Overlay\n" +
               "- effectIntensity: 0.0 to 1.0\n" +
               "- captionText: short punchy meme text caption (e.g. 'BRO THOUGHT', 'WAIT FOR IT', 'EMOTIONAL DAMAGE', 'POV: MONDAY')\n" +
               "- captionPosition: one of 'Top', 'Bottom', 'Center'\n\n" +
               "Example format:\n" +
               "[{\"timestampMs\": 3000, \"soundId\": \"...\", \"soundIndex\": 0, \"actionVectorTags\": [\"explosion\"], " +
               "\"sceneDescription\": \"A character stumbles backward after being hit.\", " +
               "\"selectionRationale\": \"Comedic punch punctuates the physical chaos.\", " +
               "\"isIronic\": false, \"visualEffect\": \"SnapZoom\", \"effectIntensity\": 0.8, " +
               "\"captionText\": \"BRO THOUGHT\", \"captionPosition\": \"Top\"}]\n" +
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

        if (json.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("entries", out var entriesEl))
                {
                    json = entriesEl.GetRawText();
                }
            }
            catch { }
        }

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

        SoundId ResolveSound(DirectorEntry entry)
        {
            if (entry.SoundIndex.HasValue && topCandidates is not null
                && entry.SoundIndex.Value >= 0 && entry.SoundIndex.Value < topCandidates.Count)
            {
                return topCandidates[entry.SoundIndex.Value].SoundId;
            }

            var raw = entry.SoundIdRaw;
            if (int.TryParse(raw, out var idx) && topCandidates is not null
                && idx >= 0 && idx < topCandidates.Count)
            {
                return topCandidates[idx].SoundId;
            }

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
            SoundId = ResolveSound(e),
            ActionVectorTags = e.ActionVectorTags,
            SceneDescription = e.SceneDescription,
            SelectionRationale = e.SelectionRationale,
            IsIronic = e.IsIronic,
            VisualEffect = e.VisualEffect,
            EffectIntensity = e.EffectIntensity,
            PlacementType = PlacementType.Triggered,
            CaptionText = e.CaptionText,
            CaptionPosition = e.CaptionPosition,
        }).ToArray();
    }

    private sealed class DirectorEntry
    {
        [JsonPropertyName("timestampMs")] public long TimestampMs { get; init; }
        [JsonPropertyName("soundId")] public string SoundIdRaw { get; init; } = string.Empty;
        [JsonPropertyName("soundIndex")] public int? SoundIndex { get; init; }
        [JsonPropertyName("actionVectorTags")] public string[] ActionVectorTags { get; init; } = [];
        [JsonPropertyName("sceneDescription")] public string SceneDescription { get; init; } = string.Empty;
        [JsonPropertyName("selectionRationale")] public string SelectionRationale { get; init; } = string.Empty;
        [JsonPropertyName("isIronic")] public bool IsIronic { get; init; }
        [JsonPropertyName("visualEffect")] public VisualEffectType? VisualEffect { get; init; }
        [JsonPropertyName("effectIntensity")] public double? EffectIntensity { get; init; }
        [JsonPropertyName("captionText")] public string? CaptionText { get; init; }
        [JsonPropertyName("captionPosition")] public string? CaptionPosition { get; init; }
    }
}
