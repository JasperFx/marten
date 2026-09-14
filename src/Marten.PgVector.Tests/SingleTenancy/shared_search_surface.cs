using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Documents;
using JasperFx.Events.Vectors;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     The store-neutral search surface — <c>IDocumentReadOperations.Search</c>, its <c>filter</c>
///     predicate, and the implicit predicates a search has to apply (jasperfx#842, #843).
/// </summary>
[Collection("Marten.PgVector")]
public class shared_search_surface: IAsyncLifetime
{
    private DocumentStore _store = null!;

    private static ReadOnlyMemory<float> Query => new[] { 1.0f, 0.0f, 0.0f };

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_shared_surface";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.Schema.For<Memo>().SoftDeleted().FullTextIndex();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();
        session.Store(
            new Memo { Id = Guid.NewGuid(), Name = "Near", Category = "red", Embedding = [1.0f, 0.0f, 0.0f], Body = "quantum sensor" },
            new Memo { Id = Guid.NewGuid(), Name = "Middle", Category = "blue", Embedding = [0.7f, 0.7f, 0.0f], Body = "quantum widget", Children = [new MemoChild { Label = "tagged" }] },
            new Memo { Id = Guid.NewGuid(), Name = "Far", Category = "blue", Embedding = [0.0f, 0.0f, 1.0f], Body = "quantum catalogue" },
            new Memo { Id = Guid.NewGuid(), Name = "Deleted", Category = "red", Embedding = [1.0f, 0.0f, 0.0f], Body = "quantum sensor" });

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var deleting = _store.LightweightSession();
        deleting.DeleteWhere<Memo>(x => x.Name == "Deleted");
        await deleting.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    ///     The whole point of the accessor: a caller holding only the store-agnostic
    ///     <see cref="IDocumentReadOperations" /> can run a vector search without naming Marten.
    /// </summary>
    [Fact]
    public async Task reachable_from_the_store_agnostic_session()
    {
        await using var session = _store.QuerySession();

        #region sample_pgvector_neutral_search_accessor
        // IDocumentReadOperations is JasperFx's store-agnostic session contract — no Marten type
        // appears in this code, so the same method body runs against Polecat or Fisher.
        IDocumentReadOperations operations = session;

        var matches = await operations.Search.VectorSearchWithScoresAsync<Memo>(
            x => x.Embedding, Query, limit: 2);
        #endregion

        matches.Count.ShouldBe(2);
        matches[0].Document.Name.ShouldBe("Near");
    }

    /// <summary>
    ///     A store with no vector search says so, rather than answering with an empty list. The
    ///     accessor's throwing default is what makes adopting the member additive for every other
    ///     store.
    /// </summary>
    [Fact]
    public async Task a_store_without_pgvector_refuses_rather_than_returning_nothing()
    {
        using var plain = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_shared_surface_off";
        });

        await using var session = plain.QuerySession();

        var ex = Should.Throw<NotSupportedException>(() => ((IDocumentReadOperations)session).Search);
        ex.Message.ShouldContain("UsePgVector");
    }

    /// <summary>
    ///     ⚠️ The filter is applied BEFORE the limit, so the result is the top-k of the filtered set
    ///     rather than the filtered remains of the top-k. Asking for one "blue" memo out of a corpus
    ///     whose single nearest document is red is the case that tells the two apart: a post-filter
    ///     would return nothing at all.
    /// </summary>
    [Fact]
    public async Task the_filter_is_applied_before_the_limit()
    {
        await using var session = _store.QuerySession();

        #region sample_pgvector_search_filter
        var matches = await ((IDocumentReadOperations)session).Search.VectorSearchWithScoresAsync<Memo>(
            x => x.Embedding, Query, limit: 1, filter: x => x.Category == "blue");
        #endregion

        matches.Count.ShouldBe(1);
        matches[0].Document.Name.ShouldBe("Middle");
    }

    /// <summary>
    ///     The filter reaches BOTH legs of a hybrid search, before each leg's candidate depth —
    ///     otherwise rows the caller will discard consume the depth and the fused order ranks a set
    ///     that includes them.
    /// </summary>
    [Fact]
    public async Task the_filter_reaches_a_hybrid_search()
    {
        await using var session = _store.QuerySession();

        var matches = await ((IDocumentReadOperations)session).Search.HybridSearchWithScoresAsync<Memo>(
            x => x.Embedding, "quantum", Query, limit: 10, filter: x => x.Category == "blue");

        matches.Select(x => x.Document.Name).OrderBy(x => x).ShouldBe(["Far", "Middle"]);
    }

    /// <summary>
    ///     ⚠️ A soft-deleted document was returned by a Marten vector search, while Polecat and Fisher
    ///     both filtered it — the legs were hand-written SQL that never had the predicate. Routing the
    ///     WHERE through the document storage's own <c>FilterDocuments</c> is what fixes it, so the
    ///     searches now filter exactly what <c>Query&lt;T&gt;()</c> filters.
    /// </summary>
    [Fact]
    public async Task a_soft_deleted_document_is_not_a_match()
    {
        await using var session = _store.QuerySession();

        var vector = await session.VectorSearchAsync<Memo>(x => x.Embedding, Query, limit: 10);
        vector.Select(x => x.Name).ShouldNotContain("Deleted");
        vector.Count.ShouldBe(3);

        var hybrid = await session.HybridSearchAsync<Memo>(x => x.Embedding, "quantum", Query, limit: 10);
        hybrid.Select(x => x.Name).ShouldNotContain("Deleted");
    }

    /// <summary>
    ///     "Supports and refuses exactly what the store's own <c>Query&lt;T&gt;()</c> does" is the
    ///     contract's promise, so a predicate over a child collection has to work here too — it is
    ///     parsed by the same <c>WhereClauseParser</c> and lands as the same jsonpath predicate.
    /// </summary>
    [Fact]
    public async Task a_child_collection_predicate_reaches_the_sql()
    {
        await using var session = _store.QuerySession();

        var byLinq = await session.Query<Memo>()
            .Where(x => x.Children.Any(c => c.Label == "tagged"))
            .ToListAsync(TestContext.Current.CancellationToken);

        var bySearch = await ((IDocumentReadOperations)session).Search.VectorSearchWithScoresAsync<Memo>(
            x => x.Embedding, Query, limit: 10, filter: x => x.Children.Any(c => c.Label == "tagged"));

        byLinq.Count.ShouldBe(1);
        bySearch.Select(x => x.Document.Name).ShouldBe(byLinq.Select(x => x.Name));
    }
}

/// <summary>
///     ⚠️ The measurement the release notes turn on: Marten's <c>HybridSearchOptions.Distance</c>
///     defaulted to <c>Cosine</c>; the shared record defaults it to null, meaning "the metric the index
///     declared". For anyone whose index is not cosine that is a DIFFERENT ordering from the same code.
/// </summary>
[Collection("Marten.PgVector")]
public class distance_default_is_the_index_metric: IAsyncLifetime
{
    private DocumentStore _store = null!;

    // Cosine and L2 disagree about this corpus, which is the only way to tell which metric ran.
    // "Aligned" is a long vector pointing exactly at the query: cosine distance 0, L2 distance 9.
    // "Close" is a short vector pointing slightly off: cosine distance ~0.005, L2 distance ~0.1.
    private static ReadOnlyMemory<float> Query => new[] { 1.0f, 0.0f, 0.0f };

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_distance_default";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.VectorIndex<Memo>(x => x.Embedding, 3, Neutral.DistanceFunction.L2);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();
        session.Store(
            new Memo { Id = Guid.NewGuid(), Name = "Aligned", Embedding = [10.0f, 0.0f, 0.0f] },
            new Memo { Id = Guid.NewGuid(), Name = "Close", Embedding = [1.0f, 0.1f, 0.0f] });
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>The shared record's default, which is the whole behavioral change in one assertion.</summary>
    [Fact]
    public void the_shared_options_no_longer_default_to_cosine()
    {
        new Neutral.HybridSearchOptions().Distance.ShouldBeNull();
    }

    /// <summary>
    ///     ⚠️ <b>Null means the index's metric, and over an L2 index that is a different answer from
    ///     the cosine one.</b> Both orderings below are of the same two documents from the same query
    ///     vector; only the metric differs.
    /// </summary>
    [Fact]
    public async Task no_metric_named_means_the_metric_the_index_declared()
    {
        await using var session = _store.QuerySession();

        var byIndex = await ((IDocumentReadOperations)session).Search
            .VectorSearchWithScoresAsync<Memo>(x => x.Embedding, Query);

        // L2: the short, slightly-off vector is nearer than the long, exactly-aligned one.
        byIndex.Select(x => x.Document.Name).ShouldBe(["Close", "Aligned"]);
    }

    /// <summary>
    ///     ⚠️ The released extension method is NOT changed, and this pins that. Its <c>distance</c>
    ///     parameter still defaults to <c>Cosine</c>, so every call written before this change keeps
    ///     the ordering it had — widening it to a nullable would be a silent behavioral change on
    ///     shipped API and, because optional arguments are baked into the caller's IL, a binary break
    ///     besides. The new behavior is reachable, deliberately, only through the new surface.
    /// </summary>
    [Fact]
    public async Task the_extension_method_still_defaults_to_cosine()
    {
        await using var session = _store.QuerySession();

        var byExtension = await session.VectorSearchAsync<Memo>(x => x.Embedding, Query);

        // Cosine: the exactly-aligned vector is nearest, whatever its magnitude.
        byExtension.Select(x => x.Name).ShouldBe(["Aligned", "Close"]);
    }

    /// <summary>
    ///     #5433: a query vector of the wrong length is refused by NAME, against the length the index
    ///     DECLARED.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Asserted over an EMPTY result rather than over the corpus, because the empty case
    ///         is the one that was silent.</b> The cast is built from the query's length, so a
    ///         two-element query casts the STORED embedding to <c>vector(2)</c> as well — which
    ///         Postgres rejects when it evaluates the operator, and never evaluates when no row
    ///         survives the <c>WHERE</c>. So before this the call returned an empty list with no
    ///         error, which is indistinguishable from a correct search over a sparse corpus. The
    ///         predicate here matches nothing on purpose.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task a_query_vector_of_the_wrong_length_is_refused()
    {
        await using var session = _store.QuerySession();

        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await ((IDocumentReadOperations)session).Search.VectorSearchWithScoresAsync<Memo>(
                x => x.Embedding, new float[] { 1.0f, 0.0f }, limit: 5,
                filter: x => x.Name == "nothing-is-called-this"));

        ex.Message.ShouldContain("has 2 dimensions");
        ex.Message.ShouldContain("declares 3");
    }

    /// <summary>
    ///     A member with no declared index is NOT refused: Marten's searches work without one — the
    ///     index is what makes them fast, not what makes them possible — so there is no declared
    ///     length to check against and refusing would break a legitimate call.
    /// </summary>
    [Fact]
    public async Task a_member_with_no_declared_index_is_not_length_checked()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_unindexed_length";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            // Registered up front so the table is in the migration below. Marten creates document
            // storage on demand, and a search is a read — it would find no table rather than an
            // empty one, which is a different failure from the one this fact is about.
            opts.RegisterDocumentType<Memo>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = store.QuerySession();

        var hits = await ((IDocumentReadOperations)session).Search.VectorSearchWithScoresAsync<Memo>(
            x => x.Embedding, new float[] { 1.0f, 0.0f }, limit: 5);

        hits.ShouldBeEmpty();
    }

    /// <summary>
    ///     Two indexes over one member for two metrics is legitimate — each serves queries the other
    ///     cannot — but then there is no single metric "the index declared", so the caller is asked
    ///     rather than guessed at.
    /// </summary>
    [Fact]
    public void two_indexes_for_one_member_make_the_default_ambiguous()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.VectorIndex<Memo>(x => x.Embedding, 3, Neutral.DistanceFunction.L2);
        options.VectorIndex<Memo>(x => x.Embedding, 3, Neutral.DistanceFunction.Cosine);

        var member = typeof(Memo).GetProperty(nameof(Memo.Embedding))!;

        var ex = Should.Throw<InvalidOperationException>(() =>
            VectorSearchRunner.ResolveDistance<Memo>(options, member, null));

        ex.Message.ShouldContain("no single metric");
    }
}

public class Memo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Body { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public List<MemoChild> Children { get; set; } = [];
    public float[]? Embedding { get; set; }
}

public class MemoChild
{
    public string Label { get; set; } = "";
}
