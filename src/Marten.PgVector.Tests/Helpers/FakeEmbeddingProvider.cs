using Marten.PgVector.Projection;
using Pgvector;

namespace Marten.PgVector.Tests.Helpers;

/// <summary>
/// Deterministic fake embedding provider for testing.
/// Generates a simple hash-based vector from the input text.
/// </summary>
public class FakeEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions { get; }

    public FakeEmbeddingProvider(int dimensions = 3)
    {
        Dimensions = dimensions;
    }

    public Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        var results = new Vector[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            results[i] = GenerateVector(texts[i]);
        }
        return Task.FromResult(results);
    }

    /// <summary>
    /// Generate a deterministic vector from text — same text always produces the same vector.
    /// </summary>
    public Vector GenerateVector(string text)
    {
        var hash = text.GetHashCode();
        var values = new float[Dimensions];
        for (int i = 0; i < Dimensions; i++)
        {
            // Deterministic but varied per dimension
            values[i] = (float)Math.Sin(hash + i * 7) * 0.5f + 0.5f;
        }
        return new Vector(values);
    }
}
