// SOLID: Single Responsibility — matching isolated from orchestration
using PoMemeVideo.Api.Entities;
using PoMemeVideo.Api.Interfaces;
using System.Numerics.Tensors;

namespace PoMemeVideo.Api.MemeLibrary;

public sealed record SoundCandidate(SoundAsset Sound, float Score);

public sealed class SemanticMatchingService
{
    private readonly ISoundAssetRepository _repository;

    public SemanticMatchingService(ISoundAssetRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<SoundCandidate>> GetTopCandidatesAsync(
        string actionLabel,
        int topN = 3,
        CancellationToken cancellationToken = default)
    {
        var allSounds = await _repository.LoadAllAsync(cancellationToken);
        if (allSounds.Count == 0) return [];

        // Build vocabulary from all unique tags across the library
        var vocabulary = allSounds
            .SelectMany(s => s.ActionVectorTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (vocabulary.Length == 0) return [];

        // Create query embedding from the action label (treated as space-separated tags)
        var queryTags = actionLabel
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var queryVector = BuildEmbedding(queryTags, vocabulary);

        // Score each sound by cosine similarity
        var candidates = allSounds
            .Select(s =>
            {
                var soundVector = BuildEmbedding(s.ActionVectorTags, vocabulary);
                var score = TensorPrimitives.CosineSimilarity<float>(queryVector, soundVector);
                return new SoundCandidate(s, score);
            })
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .Take(Math.Min(topN, allSounds.Count))
            .ToList();

        return candidates;
    }

    private static float[] BuildEmbedding(IEnumerable<string> tags, string[] vocabulary)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        var vector = new float[vocabulary.Length];
        for (var i = 0; i < vocabulary.Length; i++)
            vector[i] = tagSet.Contains(vocabulary[i]) ? 1.0f : 0.0f;

        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= magnitude;

        return vector;
    }
}
