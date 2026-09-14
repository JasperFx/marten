using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Vectors;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     The store-neutral search surface — <see cref="ReadOnlyMemory{T}" /> in,
///     <see cref="VectorMatch{T}" /> out.
/// </summary>
/// <remarks>
///     <para>
///         Marten.PgVector carried its own <c>IEmbeddingProvider</c> and <c>Neutral.DistanceFunction</c>, which
///         compiled perfectly well and made a store-agnostic caller pick a side. They are
///         <c>JasperFx.Events.Vectors</c>' now, so the same application code reads the same against
///         Marten, Polecat and Fisher — which is the whole point of the neutral contracts.
///     </para>
///     <para>
///         ⚠️ The conversion at the boundary is load-bearing and fails SILENTLY if it is dropped. The
///         provider contract hands back <c>ReadOnlyMemory&lt;float&gt;</c>, and its <c>ToString()</c> is
///         <c>"System.ReadOnlyMemory&lt;System.Single&gt;[3]"</c> — which compiles, binds as text, and is
///         not a vector literal. Only <c>Pgvector.Vector</c> knows how to render one.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class neutral_contract_search : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_neutral_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();
            opts.RegisterDocumentType<NeutralDoc>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();
        session.Store(
            new NeutralDoc { Id = Guid.NewGuid(), Name = "Near", Embedding = [1.0f, 0.0f, 0.0f] },
            new NeutralDoc { Id = Guid.NewGuid(), Name = "Middle", Embedding = [0.7f, 0.7f, 0.0f] },
            new NeutralDoc { Id = Guid.NewGuid(), Name = "Far", Embedding = [0.0f, 0.0f, 1.0f] });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private static ReadOnlyMemory<float> Query => new[] { 1.0f, 0.0f, 0.0f };

    /// <summary>
    ///     The two spellings are one search. The Pgvector-typed overload is kept for existing call
    ///     sites and forwards, so they cannot drift.
    /// </summary>
    [Fact]
    public async Task the_two_overloads_are_the_same_search()
    {
        await using var query = _store.QuerySession();

        var neutral = await query.VectorSearchAsync<NeutralDoc>(x => x.Embedding, Query);
        var pgvector = await query.VectorSearchAsync<NeutralDoc>(x => x.Embedding, new Vector(Query));

        neutral.Select(x => x.Name).ShouldBe(pgvector.Select(x => x.Name));
        neutral[0].Name.ShouldBe("Near");
    }

    /// <summary>
    ///     A scored search agrees with the unscored one about order, and hands back the distance the
    ///     ordering was done on rather than making the caller recompute it.
    /// </summary>
    [Fact]
    public async Task a_scored_search_carries_the_distance_it_ordered_on()
    {
        await using var query = _store.QuerySession();

        var scored = await query.VectorSearchWithScoresAsync<NeutralDoc>(x => x.Embedding, Query);
        var plain = await query.VectorSearchAsync<NeutralDoc>(x => x.Embedding, Query);

        scored.Select(x => x.Document.Name).ShouldBe(plain.Select(x => x.Name));
        scored[0].Document.Name.ShouldBe("Near");

        // Cosine distance to itself is 0, so the nearest is distinguishable from the rest by the
        // NUMBER and not only by its position — which is what a threshold or a confidence cut needs.
        scored[0].Distance.ShouldBeLessThan(scored[1].Distance);
    }

    /// <summary>
    ///     ⚠️ Every metric is a DISTANCE — smaller is closer — including inner product, which pgvector
    ///     returns negated for exactly that reason. One ascending sort serves all three, which is the
    ///     promise <see cref="Neutral.DistanceFunction" /> makes on every store.
    /// </summary>
    [Theory]
    [InlineData(Neutral.DistanceFunction.Cosine)]
    [InlineData(Neutral.DistanceFunction.L2)]
    [InlineData(Neutral.DistanceFunction.InnerProduct)]
    public async Task every_metric_comes_back_as_an_ascending_distance(Neutral.DistanceFunction distance)
    {
        await using var query = _store.QuerySession();

        var scored = await query.VectorSearchWithScoresAsync<NeutralDoc>(
            x => x.Embedding, Query, limit: 10, distance: distance);

        scored.Count.ShouldBe(3);
        scored.Select(x => x.Distance).ShouldBe(scored.Select(x => x.Distance).OrderBy(d => d));
        scored[0].Document.Name.ShouldBe("Near");
    }

    /// <summary>
    ///     The limit still bounds a scored search — it is the same statement, not a client-side trim.
    /// </summary>
    [Fact]
    public async Task the_limit_bounds_a_scored_search()
    {
        await using var query = _store.QuerySession();

        var scored = await query.VectorSearchWithScoresAsync<NeutralDoc>(x => x.Embedding, Query, limit: 2);

        scored.Count.ShouldBe(2);
    }
}

public class NeutralDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public float[]? Embedding { get; set; }
}
