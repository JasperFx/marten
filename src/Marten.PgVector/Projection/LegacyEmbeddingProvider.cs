using Pgvector;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Projection;

/// <summary>
///     The pre-9.36 embedding provider, in Marten.PgVector's own spelling.
/// </summary>
/// <remarks>
///     <para>
///         Kept so an existing provider implementation still compiles and can still be handed to
///         <see cref="VectorProjection" />. The shared
///         <c>JasperFx.Events.Vectors.IEmbeddingProvider</c> is what new code should implement — it is
///         the one Polecat and Fisher also take, so a provider written against it works with all three.
///     </para>
///     <para>
///         ⚠️ The difference is not cosmetic. This returns <see cref="Vector" />; the shared one returns
///         <c>ReadOnlyMemory&lt;float&gt;</c>, whose <c>ToString()</c> is
///         <c>"System.ReadOnlyMemory&lt;System.Single&gt;[n]"</c> rather than a vector literal. The
///         adapter converts rather than letting either shape reach a parameter unconverted.
///     </para>
/// </remarks>
[Obsolete(
    "Implement JasperFx.Events.Vectors.IEmbeddingProvider instead, which Marten, Polecat and Fisher "
    + "all take. This interface still works and is adapted.")]
public interface IEmbeddingProvider
{
    int Dimensions { get; }

    Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default);
}

/// <summary>
///     Presents a legacy <see cref="IEmbeddingProvider" /> as the shared contract, so there is exactly
///     one embedding path inside the package rather than two.
/// </summary>
[Obsolete("Only needed while the legacy IEmbeddingProvider is in use.")]
internal sealed class LegacyEmbeddingProviderAdapter(IEmbeddingProvider inner): Neutral.IEmbeddingProvider
{
    public int Dimensions => inner.Dimensions;

    public async Task<ReadOnlyMemory<float>[]> GenerateEmbeddingsAsync(
        string[] texts, CancellationToken ct = default)
    {
        var vectors = await inner.GenerateEmbeddingsAsync(texts, ct).ConfigureAwait(false);
        return vectors.Select(x => x.Memory).ToArray();
    }
}
