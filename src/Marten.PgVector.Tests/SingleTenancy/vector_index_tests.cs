using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Vectors;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     The HNSW index declared by <c>StoreOptions.VectorIndex</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>"The index exists" is the assertion that proves nothing here.</b> An expression index
///         whose expression does not match the query's, or whose operator class does not match the
///         metric, is created without error, reported nowhere, and never used — the query just stays a
///         sequential scan. So the load-bearing test reads the PLAN, not the catalog.
///     </para>
///     <para>
///         Measured against pgvector 0.8.5 before this was built: a <c>vector_cosine_ops</c> index serves
///         a <c>&lt;=&gt;</c> query as an <c>Index Scan</c>, and a <c>vector_l2_ops</c> index over the
///         same expression serves the same query as a <c>Seq Scan</c>.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class vector_index_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_index_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();
            opts.RegisterDocumentType<IndexedDoc>();

            #region sample_pgvector_vector_index
            // An HNSW index over (data ->> 'Embedding')::vector(3) with vector_cosine_ops.
            // distance defaults to Cosine; m and efConstruction default to pgvector's own defaults.
            opts.VectorIndex<IndexedDoc>(x => x.Embedding, dimensions: 3, m: 16, efConstruction: 64);
            #endregion
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Enough rows that a sequential scan is not simply the cheaper plan — otherwise the planner
        // choosing one says nothing about whether the index is usable.
        var docs = Enumerable.Range(0, 2500).Select(i => new IndexedDoc
        {
            Id = Guid.NewGuid(),
            Name = $"doc-{i}",
            Embedding = [(float)Math.Sin(i), (float)Math.Cos(i), i % 7 / 7f]
        }).ToArray();

        await _store.BulkInsertAsync(docs);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var analyze = conn.CreateCommand();
        analyze.CommandText = "analyze pgvector_index_tests.mt_doc_indexeddoc";
        await analyze.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private static async Task<string> ExplainAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "explain (costs off) " + sql;
        await using var reader = await cmd.ExecuteReaderAsync();

        var lines = new System.Text.StringBuilder();
        while (await reader.ReadAsync()) lines.AppendLine(reader.GetString(0));
        return lines.ToString();
    }

    /// <summary>
    ///     The index is created, and it is an HNSW index with the cosine operator class — both halves
    ///     read off the catalog's own rendering of the definition.
    /// </summary>
    [Fact]
    public async Task the_index_is_created_as_hnsw_with_the_matching_operator_class()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select indexdef from pg_indexes where schemaname = 'pgvector_index_tests' and indexname like '%embedding%'";

        var definition = (string?)await cmd.ExecuteScalarAsync();

        definition.ShouldNotBeNull();
        definition.ShouldContain("USING hnsw");
        definition.ShouldContain("vector_cosine_ops");
        definition.ShouldContain("::vector(3)");
        definition.ShouldContain("m='16'");
        definition.ShouldContain("ef_construction='64'");
    }

    /// <summary>
    ///     ⚠️ The one that matters: the planner actually reaches the index for the statement
    ///     <c>VectorSearchAsync</c> emits. Everything else in this file passes against an index that is
    ///     created and never used.
    /// </summary>
    [Fact]
    public async Task the_planner_uses_the_index_for_the_search_statement()
    {
        var plan = await ExplainAsync(
            "select d.data from pgvector_index_tests.mt_doc_indexeddoc d " +
            "where d.data->>'Embedding' is not null " +
            "order by (d.data->>'Embedding')::vector(3) <=> '[1,0,0]'::vector(3) limit 10");

        plan.ShouldContain("Index Scan");
        plan.ShouldNotContain("Seq Scan");
    }

    /// <summary>
    ///     ⚠️ The trap this design exists to avoid, pinned as a fact rather than left in a comment: an
    ///     index built for one metric does NOT serve a query in another.
    /// </summary>
    /// <remarks>
    ///     The index here is <c>vector_cosine_ops</c>. Asking the same expression for L2 distance
    ///     (<c>&lt;-&gt;</c>) falls back to a sequential scan, with no error anywhere — which is exactly
    ///     what a hand-written index declaration gets wrong, and why the operator class and the query
    ///     operator are both derived from <see cref="Neutral.DistanceFunction" />.
    /// </remarks>
    [Fact]
    public async Task an_index_for_another_metric_does_not_serve_this_query()
    {
        var plan = await ExplainAsync(
            "select d.data from pgvector_index_tests.mt_doc_indexeddoc d " +
            "order by (d.data->>'Embedding')::vector(3) <-> '[1,0,0]'::vector(3) limit 10");

        plan.ShouldContain("Seq Scan");
    }

    /// <summary>
    ///     The search still returns the right answer through the index — an index scan that ordered
    ///     wrongly would satisfy the plan assertion above and be worse than no index at all.
    /// </summary>
    [Fact]
    public async Task the_search_is_still_correct_through_the_index()
    {
        await using var session = _store.LightweightSession();
        var marker = new IndexedDoc { Id = Guid.NewGuid(), Name = "exact", Embedding = [1.0f, 0.0f, 0.0f] };
        session.Store(marker);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var query = _store.QuerySession();
        var results = await query.VectorSearchAsync<IndexedDoc>(
            x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), limit: 1);

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("exact");
    }

    /// <summary>
    ///     The dimensions are part of the indexed expression, so a store that declares none has declared
    ///     an index that can serve nothing. Refused rather than emitted.
    /// </summary>
    [Fact]
    public void dimensions_are_required()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UsePgVector();
            opts.VectorIndex<IndexedDoc>(x => x.Embedding, dimensions: 0);
        }));
    }
}

public class IndexedDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public float[]? Embedding { get; set; }
}
