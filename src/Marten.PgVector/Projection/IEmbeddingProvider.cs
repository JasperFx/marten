using Pgvector;

namespace Marten.PgVector.Projection;

/// <summary>
/// User-supplied embedding generator. Implement this with your
/// chosen model (OpenAI, Ollama, local model, etc.).
/// Marten.PgVector is AI-model-agnostic.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// The dimensionality of the vectors produced by this provider.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Generate embeddings for one or more text inputs.
    /// The returned array must have the same length as the input array.
    /// </summary>
    Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
}
