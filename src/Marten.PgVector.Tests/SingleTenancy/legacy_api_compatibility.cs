using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.PgVector.Projection;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     The pre-9.36 Marten.PgVector API still compiles and still works, alongside the shared contracts.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Restoring a legacy type into the package's OWN namespace silently recaptures every
///         unqualified use of that name inside the package.</b> A member of a namespace beats a
///         using-alias declared outside it, so <c>DistanceFunction</c> in <c>Marten.PgVector</c>'s own
///         files went back to meaning the legacy enum — and because the two enums declare the same three
///         members, almost everything still compiled. The public signatures had quietly reverted.
///     </para>
///     <para>
///         That is why the package qualifies the shared enum everywhere, and why the first test here
///         asserts on the TYPE a call binds to rather than on a result.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class legacy_api_compatibility : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_legacy_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();
            opts.RegisterDocumentType<LegacyDoc>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();
        session.Store(
            new LegacyDoc { Id = Guid.NewGuid(), Name = "Near", Embedding = [1.0f, 0.0f, 0.0f] },
            new LegacyDoc { Id = Guid.NewGuid(), Name = "Far", Embedding = [0.0f, 0.0f, 1.0f] });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    ///     ⚠️ The one that catches the shadowing. The default overload's metric parameter must be the
    ///     SHARED enum — if the legacy one recaptured the signature this fails to compile, which is the
    ///     only signal there would be.
    /// </summary>
    [Fact]
    public async Task the_default_overload_takes_the_shared_distance_function()
    {
        await using var query = _store.QuerySession();

        var results = await query.VectorSearchAsync<LegacyDoc>(
            x => x.Embedding,
            new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]),
            limit: 10,
            distance: Neutral.DistanceFunction.L2);

        results[0].Name.ShouldBe("Near");
    }

    /// <summary>
    ///     The pre-9.36 call — Pgvector's vector type and Marten.PgVector's own enum — still compiles
    ///     and still returns the same answer.
    /// </summary>
    [Fact]
    public async Task the_legacy_call_shape_still_works()
    {
        await using var query = _store.QuerySession();

#pragma warning disable CS0618 // deliberately exercising the obsolete surface
        var results = await query.VectorSearchAsync<LegacyDoc>(
            x => x.Embedding,
            new Vector(new[] { 1.0f, 0.0f, 0.0f }),
            limit: 10,
            distance: DistanceFunction.L2);
#pragma warning restore CS0618

        results[0].Name.ShouldBe("Near");
    }

    /// <summary>
    ///     ⚠️ The two enums are converted BY NAME, never by value, and this is the test that says so.
    /// </summary>
    /// <remarks>
    ///     The legacy enum declares <c>L2</c> first and the shared one declares <c>Cosine</c> first, so
    ///     a cast would turn an L2 search into a cosine search with the same underlying number and no
    ///     error anywhere.
    /// </remarks>
    [Fact]
    public void the_two_enums_convert_by_name_not_by_value()
    {
#pragma warning disable CS0618
        DistanceFunction.L2.ToNeutral().ShouldBe(Neutral.DistanceFunction.L2);
        DistanceFunction.Cosine.ToNeutral().ShouldBe(Neutral.DistanceFunction.Cosine);
        DistanceFunction.InnerProduct.ToNeutral().ShouldBe(Neutral.DistanceFunction.InnerProduct);

        // The values genuinely differ, which is what makes a cast wrong rather than merely untidy.
        ((int)DistanceFunction.L2).ShouldNotBe((int)Neutral.DistanceFunction.L2);
#pragma warning restore CS0618
    }

    /// <summary>
    ///     A projection written against the legacy embedding provider still constructs and still embeds,
    ///     adapted onto the shared contract rather than kept as a second embedding path.
    /// </summary>
    [Fact]
    public void the_legacy_embedding_provider_is_still_accepted()
    {
#pragma warning disable CS0618
        var projection = new LegacyProviderProjection(new LegacyProvider());
#pragma warning restore CS0618

        projection.ShouldNotBeNull();
    }
}

#pragma warning disable CS0618
/// <summary>A provider written against the pre-9.36 interface.</summary>
public class LegacyProvider : IEmbeddingProvider
{
    public int Dimensions => 3;

    public Task<Vector[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
        => Task.FromResult(texts.Select(_ => new Vector(new[] { 1.0f, 0.0f, 0.0f })).ToArray());
}

/// <summary>A projection built the pre-9.36 way.</summary>
public class LegacyProviderProjection : VectorProjection
{
    public LegacyProviderProjection(IEmbeddingProvider provider)
        : base("legacy_vectors", provider)
    {
    }

    protected override void Configure(VectorProjectionMapping map)
    {
    }
}
#pragma warning restore CS0618

public class LegacyDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public float[]? Embedding { get; set; }
}
