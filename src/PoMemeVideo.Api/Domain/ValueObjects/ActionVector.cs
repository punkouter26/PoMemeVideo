// GoF: Value Object pattern
namespace PoMemeVideo.Domain.ValueObjects;

public record ActionVector(string[] Tags)
{
    /// <summary>
    /// Converts tags to a bag-of-words embedding vector aligned to the provided vocabulary.
    /// Each dimension is 1.0f if the vocabulary word appears in Tags, otherwise 0.0f.
    /// </summary>
    public float[] ToEmbedding(string[] vocabulary)
    {
        var tagSet = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase);
        var embedding = new float[vocabulary.Length];
        for (var i = 0; i < vocabulary.Length; i++)
        {
            embedding[i] = tagSet.Contains(vocabulary[i]) ? 1.0f : 0.0f;
        }
        return embedding;
    }
}
