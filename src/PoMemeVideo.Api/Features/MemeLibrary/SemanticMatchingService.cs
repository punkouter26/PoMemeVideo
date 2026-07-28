// SOLID: Single Responsibility — matching isolated from orchestration
using System.Numerics.Tensors;

namespace PoMemeVideo.Api.Features.MemeLibrary;

/// <summary>
/// Ranks the sound library against a vision label.
/// </summary>
/// <remarks>
/// Two properties of the real input drive this design:
///
/// 1. <b>Vision labels are natural-language captions</b> ("man dancing or shadowboxing"), not tags.
///    Matching them against the tag vocabulary alone found nothing — measured against the live
///    library, all 7 captions from a real run scored 0.000 against all 202 sounds, so the
///    <c>Score &gt; 0</c> filter returned an empty candidate list every time and the director only
///    ever saw the fallback priority sounds. <see cref="SoundAsset.DisplayName"/> is the untapped
///    signal here: it is unique per sound and its words ("Coffin Dance Meme") are exactly the
///    vocabulary captions use. Indexing tags ∪ name tokens took the library from 41 distinct
///    vectors to 191, and the worst tie group from 97 sounds to 11.
///
/// 2. <b>Tag frequency is extremely skewed</b> — "funny" sits on 71% of the library and "reaction"
///    on 65%, so under binary weighting they dominate the score while carrying almost no
///    information. Weighting each term by inverse document frequency lets the rare, discriminating
///    words decide the ranking instead.
///
/// The index is built once per scope (one engine run) rather than per label — previously the
/// vocabulary and all 202 document vectors were rebuilt on every call.
/// </remarks>
public sealed class SemanticMatchingService : ISemanticMatchingService
{
    // Curated priority sounds (wojak-storytelling staples) outrank generic matches
    // with comparable overlap, without letting a zero-overlap sound win.
    private const float PriorityBoost = 1.5f;

    /// <summary>Tokens shorter than this are noise ("a", "of", "in") and are dropped.</summary>
    private const int MinTokenLength = 3;

    private readonly ISoundAssetRepository _repository;

    // Memoised for the lifetime of the scope. The service is Scoped, so one engine run reuses a
    // single index across all of its labels and a later run always rebuilds from a fresh load.
    private SearchIndex? _index;

    public SemanticMatchingService(ISoundAssetRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<SoundCandidate>> GetTopCandidatesAsync(
        string actionLabel,
        int topN = 3,
        CancellationToken cancellationToken = default)
    {
        _index ??= BuildIndex(await _repository.LoadAllAsync(cancellationToken));
        var index = _index;

        if (index.Documents.Length == 0 || index.Vocabulary.Length == 0)
            return [];

        var queryVector = index.Vectorize(Tokenize(actionLabel));
        if (queryVector is null)
            return [];  // caption shares no term with the library — the caller's fallback applies

        return index.Documents
            .Select(d =>
            {
                var score = TensorPrimitives.CosineSimilarity<float>(queryVector, d.Vector);
                if (d.Sound.Priority)
                    score *= PriorityBoost;
                return new SoundCandidate(d.Sound, score);
            })
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            // Deterministic tie-break: without it, equal-scoring sounds fall back to storage
            // order, so the same few always win and the rest of the library is never reachable.
            .ThenBy(c => c.Sound.SoundId.Value)
            .Take(Math.Min(topN, index.Documents.Length))
            .ToList();
    }

    /// <summary>Splits free text into lowercase word tokens, dropping short noise words.</summary>
    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .Split(t => !char.IsLetterOrDigit(t))
            .Where(t => t.Length >= MinTokenLength);
    }

    private static SearchIndex BuildIndex(IReadOnlyList<SoundAsset> sounds)
    {
        if (sounds.Count == 0)
            return new SearchIndex([], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), [], []);

        // A sound's terms are its curated tags plus the words of its display name.
        var terms = sounds
            .Select(s => s.ActionVectorTags
                .Concat(Tokenize(s.DisplayName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var vocabulary = terms
            .SelectMany(t => t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lookup = new Dictionary<string, int>(vocabulary.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < vocabulary.Length; i++)
            lookup[vocabulary[i]] = i;

        var documentFrequency = new int[vocabulary.Length];
        foreach (var termSet in terms)
            foreach (var term in termSet)
                documentFrequency[lookup[term]]++;

        // Smoothed idf: log((1 + N) / (1 + df)) + 1. The smoothing is not cosmetic — plain
        // log(N / df) evaluates to exactly 0 for a term carried by *every* sound, which zeroes
        // those vectors and makes the whole library unreachable. That is a real case in a small
        // or narrowly-tagged library. The "+ 1" floor keeps a universal term weakly informative
        // instead of worthless, while a rare term still outweighs a ubiquitous one ~4x on the
        // live library ("funny" 1.34 vs a one-off tag 5.62).
        var idf = new float[vocabulary.Length];
        for (var i = 0; i < idf.Length; i++)
            idf[i] = MathF.Log((1f + sounds.Count) / (1f + documentFrequency[i])) + 1f;

        var index = new SearchIndex(vocabulary, lookup, idf, []);

        var documents = new (SoundAsset, float[])[sounds.Count];
        for (var i = 0; i < sounds.Count; i++)
            documents[i] = (sounds[i], index.Vectorize(terms[i]) ?? new float[vocabulary.Length]);

        return index with { Documents = documents };
    }

    /// <summary>
    /// The library index: vocabulary, per-term idf weights, and one L2-normalised vector per sound.
    /// </summary>
    private sealed record SearchIndex(
        string[] Vocabulary,
        Dictionary<string, int> Lookup,
        float[] Idf,
        (SoundAsset Sound, float[] Vector)[] Documents)
    {
        /// <summary>
        /// Builds an idf-weighted, L2-normalised vector. Returns null when no term is in the
        /// vocabulary, which is a genuine "no signal" answer rather than a zero vector.
        /// </summary>
        public float[]? Vectorize(IEnumerable<string> tokens)
        {
            var vector = new float[Vocabulary.Length];
            var matched = false;

            foreach (var token in tokens)
            {
                if (!Lookup.TryGetValue(token, out var i))
                    continue;
                vector[i] = Idf[i];
                matched = true;
            }

            if (!matched)
                return null;

            var magnitude = MathF.Sqrt(TensorPrimitives.SumOfSquares<float>(vector));
            if (magnitude > 0)
                TensorPrimitives.Divide(vector, magnitude, vector);

            return vector;
        }
    }
}

file static class TokenizerExtensions
{
    /// <summary>Splits on a character predicate, dropping empty segments.</summary>
    public static IEnumerable<string> Split(this string text, Func<char, bool> isSeparator)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && !isSeparator(text[i]))
                continue;

            if (i > start)
                yield return text[start..i].ToLowerInvariant();

            start = i + 1;
        }
    }
}
