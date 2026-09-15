using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

public class HierarchyContent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public float[]? Embedding { get; set; }
}

public class HierarchyArticle: HierarchyContent
{
    public string Author { get; set; } = "";
}

public class HierarchyVideo: HierarchyContent
{
    public int Seconds { get; set; }
}

/// <summary>
///     #5440: vector and hybrid search over a document hierarchy.
/// </summary>
/// <remarks>
///     <para>
///         Two facts, and they are different fixes. A search for a SUBCLASS returns only that subclass,
///         which is the <c>mt_doc_type</c> filter #5427 added. A search for the BASE type returns each
///         row as its concrete subtype, which is what <c>Query&lt;T&gt;()</c> does and what the searches
///         did not: they deserialised <c>d.data</c> as <c>T</c>, so every article and video came back as
///         a plain content item with its subclass members gone.
///     </para>
///     <para>
///         The corpus is one of each, all matching the text and ordered by distance from the query, so
///         every result's position and type is known in advance.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class hierarchy_search: IAsyncLifetime
{
    private DocumentStore _store = null!;

    private static ReadOnlyMemory<float> Query => new[] { 1.0f, 0.0f, 0.0f };

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_hierarchy";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.Schema.For<HierarchyContent>()
                .AddSubClass<HierarchyArticle>()
                .AddSubClass<HierarchyVideo>()
                .FullTextIndex();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();

        session.Store(new HierarchyContent
        {
            Id = Guid.NewGuid(), Title = "quantum notes", Embedding = [1.0f, 0.0f, 0.0f]
        });
        session.Store(new HierarchyArticle
        {
            Id = Guid.NewGuid(), Title = "quantum article", Author = "Ada", Embedding = [0.9f, 0.1f, 0.0f]
        });
        session.Store(new HierarchyVideo
        {
            Id = Guid.NewGuid(), Title = "quantum video", Seconds = 42, Embedding = [0.6f, 0.4f, 0.0f]
        });

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    [Fact]
    public async Task a_base_type_vector_search_returns_each_row_as_its_concrete_subtype()
    {
        await using var session = _store.QuerySession();

        var matches = await session.VectorSearchWithScoresAsync<HierarchyContent>(
            x => x.Embedding, Query, limit: 3);

        matches.Select(x => x.Document.GetType())
            .ShouldBe([typeof(HierarchyContent), typeof(HierarchyArticle), typeof(HierarchyVideo)]);

        matches.Select(x => x.Document).OfType<HierarchyArticle>().Single().Author.ShouldBe("Ada");
        matches.Select(x => x.Document).OfType<HierarchyVideo>().Single().Seconds.ShouldBe(42);

        // The scores still line up with the rows they came from, now that they are read after the
        // storage's columns rather than from a fixed second ordinal.
        matches.Select(x => x.Distance).ShouldBeInOrder(SortDirection.Ascending);
        matches[0].Distance.ShouldBe(0, 1e-6);
    }

    /// <summary>
    ///     The same types LINQ hands back for the same rows, which is the parity the fix is for.
    /// </summary>
    [Fact]
    public async Task a_base_type_vector_search_agrees_with_query_on_the_types()
    {
        await using var session = _store.QuerySession();

        var searched = await session.VectorSearchAsync<HierarchyContent>(x => x.Embedding, Query, limit: 3);
        var queried = await session.Query<HierarchyContent>().ToListAsync(TestContext.Current.CancellationToken);

        searched.Select(x => (x.Id, x.GetType())).OrderBy(x => x.Id)
            .ShouldBe(queried.Select(x => (x.Id, x.GetType())).OrderBy(x => x.Id));
    }

    [Fact]
    public async Task a_subclass_vector_search_returns_only_that_subclass()
    {
        await using var session = _store.QuerySession();

        var matches = await session.VectorSearchAsync<HierarchyArticle>(x => x.Embedding, Query, limit: 10);

        matches.ShouldHaveSingleItem().Author.ShouldBe("Ada");
    }

    [Fact]
    public async Task a_base_type_hybrid_search_returns_each_row_as_its_concrete_subtype()
    {
        await using var session = _store.QuerySession();

        var matches = await session.HybridSearchAsync<HierarchyContent>(
            x => x.Embedding, "quantum", Query, limit: 3, token: TestContext.Current.CancellationToken);

        matches.Count.ShouldBe(3);
        matches.OfType<HierarchyArticle>().Single().Author.ShouldBe("Ada");
        matches.OfType<HierarchyVideo>().Single().Seconds.ShouldBe(42);
        matches.Count(x => x.GetType() == typeof(HierarchyContent)).ShouldBe(1);
    }

    [Fact]
    public async Task a_subclass_hybrid_search_returns_only_that_subclass()
    {
        await using var session = _store.QuerySession();

        var matches = await session.HybridSearchAsync<HierarchyVideo>(
            x => x.Embedding, "quantum", Query, limit: 10, token: TestContext.Current.CancellationToken);

        matches.ShouldHaveSingleItem().Seconds.ShouldBe(42);
    }
}
