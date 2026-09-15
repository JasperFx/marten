using JasperFx.Events.Vectors;
using Marten.PgVector.Projection;
using Pgvector;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.Helpers;

/// <summary>
/// Deterministic fake embedding provider for testing.
/// Generates a simple hash-based vector from the input text.
/// </summary>
public class FakeEmbeddingProvider : Neutral.IEmbeddingProvider
{
    public int Dimensions { get; }

    public FakeEmbeddingProvider(int dimensions = 3)
    {
        Dimensions = dimensions;
    }

    /// <summary>
    ///     Every text handed to the model, in order, across all calls.
    /// </summary>
    /// <remarks>
    ///     A model call is the expensive thing a vector projection does, so "how many times was this
    ///     embedded" is a fact worth asserting — see marten#5422, where a page holding two writes for one
    ///     id should cost one call rather than two.
    /// </remarks>
    public List<string> RequestedTexts { get; } = [];

    public Task<ReadOnlyMemory<float>[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        RequestedTexts.AddRange(texts);

        var results = new ReadOnlyMemory<float>[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            results[i] = GenerateVector(texts[i]).Memory;
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
