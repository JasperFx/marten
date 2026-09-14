using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using System.Collections.Generic;
using Marten.PgVector.Projection;
using Marten.PgVector.Tests.Helpers;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.ConjoinedTenancy;

public record ArticleWritten(Guid ArticleId, string Body);

public record ArticleRetracted(Guid ArticleId);

public class ArticleVectorProjection: VectorProjection<Guid>
{
    public ArticleVectorProjection(Neutral.IEmbeddingProvider provider)
        : base("article_vectors", provider)
    {
    }

    protected override void Configure(Neutral.VectorProjectionMap<Guid> map)
    {
        map.Map<ArticleWritten>(e => e.Data.Body, e => e.Data.ArticleId);
        map.Delete<ArticleRetracted>(e => e.Data.ArticleId);
    }
}

/// <summary>
///     marten#5420: <see cref="VectorProjection{TId}" /> under conjoined tenancy, where every tenant's
///     embeddings share one table.
/// </summary>
/// <remarks>
///     <para>
///         <b>Every fact here turns on two tenants owning a stream with the SAME id</b>, which is
///         ordinary rather than contrived: under conjoined tenancy <c>mt_streams</c> is keyed
///         <c>(tenant_id, id)</c>, so two tenants may each own a stream with that Guid — and the
///         default id selector is the stream id.
///     </para>
///     <para>
///         ⚠️ <b>All three failures are silent.</b> The projection runs, reports healthy, and returns
///         plausible results. The only existing conjoined coverage,
///         <c>conjoined_vector_tests</c>, exercises <c>VectorSearchAsync</c> over documents, which
///         goes through Marten's document storage and was always tenant-filtered — the projection's
///         table is not a document table and had no filter at all.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class conjoined_vector_projection_tests: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private FakeEmbeddingProvider _embedder = null!;

    public async ValueTask InitializeAsync()
    {
        _embedder = new FakeEmbeddingProvider(dimensions: 3);
        var projection = new ArticleVectorProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_conjoined_proj";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePgVector();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            opts.Projections.Add(projection, ProjectionLifecycle.Inline);

            // BuildTable(opts) rather than BuildTable(schemaName): the overload that takes the options
            // reads the tenancy off them, so the table is keyed (tenant_id, id). This is the whole
            // registration-side fix, and ValidateConfiguration refuses the other spelling here.
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable(opts));

            opts.Events.AddEventType<ArticleWritten>();
            opts.Events.AddEventType<ArticleRetracted>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private async Task writeAsync(string tenantId, Guid articleId, string body)
    {
        await using var session = _store.LightweightSession(tenantId);
        session.Events.StartStream(articleId, new ArticleWritten(articleId, body));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<VectorProjectionMatch<Guid>>> searchAsync(string tenantId, string text)
    {
        var embeddings = await _embedder.GenerateEmbeddingsAsync([text], TestContext.Current.CancellationToken);
        var embedding = embeddings[0];

        await using var session = _store.QuerySession(tenantId);

        return await session.VectorProjectionSearchAsync<Guid>(
            "article_vectors", embedding, limit: 10, distance: Neutral.DistanceFunction.L2);
    }

    /// <summary>
    ///     marten#5420 failure 1: the cross-tenant read. Tenant B's search returned tenant A's id and
    ///     <c>content_text</c>.
    /// </summary>
    [Fact]
    public async Task one_tenants_search_does_not_return_another_tenants_rows()
    {
        var articleId = Guid.NewGuid();
        await writeAsync("tenant_a", articleId, "the fox in the snow");

        var forB = await searchAsync("tenant_b", "the fox in the snow");

        forB.ShouldBeEmpty();

        // And the owning tenant still finds it, so this is not passing because the search broke.
        var forA = await searchAsync("tenant_a", "the fox in the snow");
        forA.Single().ContentText.ShouldBe("the fox in the snow");
    }

    /// <summary>
    ///     marten#5420 failure 2: the cross-tenant overwrite, through <c>ON CONFLICT (id)</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>The primary key is the fix here, not the read filter.</b> With <c>id</c> alone as the
    ///     key there is only ever ONE row for this article, so the later tenant's write replaces the
    ///     earlier tenant's embedding and text — and filtering the read by tenant afterwards would
    ///     simply return nothing for whichever tenant lost. Both tenants must end up with a row of
    ///     their own.
    /// </remarks>
    [Fact]
    public async Task two_tenants_sharing_a_stream_id_each_keep_their_own_content()
    {
        var articleId = Guid.NewGuid();

        await writeAsync("tenant_a", articleId, "the fox in the snow");
        await writeAsync("tenant_b", articleId, "a heron at dusk");

        var forA = await searchAsync("tenant_a", "the fox in the snow");
        var forB = await searchAsync("tenant_b", "a heron at dusk");

        forA.Single().ContentText.ShouldBe("the fox in the snow");
        forB.Single().ContentText.ShouldBe("a heron at dusk");
    }

    /// <summary>
    ///     The content-hash half of the same collision, and the one that hides itself best.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stored hash is the only thing deciding whether the embedding model is called at all.
    ///         Read across tenants, tenant B's identical content matched tenant A's hash, so B's write
    ///         was SKIPPED as unnecessary — leaving B with no row of its own and nothing anywhere to
    ///         report it. That is why the hash read is tenant-scoped too, not just the search.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Asserted by counting rows, not by searching</b>, and the first version of this test
    ///         got it wrong in a way worth recording. Searching as each tenant and checking the text
    ///         PASSED against the bug: with no tenant filter, tenant B's search returned tenant A's row,
    ///         and because the content is identical by construction the assertion could not tell "each
    ///         tenant has its own row" from "there is one shared row both tenants can see". A fact about
    ///         rows has to look at rows.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task identical_content_in_two_tenants_does_not_skip_the_second_write()
    {
        var articleId = Guid.NewGuid();

        await writeAsync("tenant_a", articleId, "the fox in the snow");
        await writeAsync("tenant_b", articleId, "the fox in the snow");

        (await tenantsHoldingAsync(articleId)).ShouldBe(["tenant_a", "tenant_b"]);
    }

    /// <summary>
    ///     The tenants that actually hold a row for this article, read straight off the table.
    /// </summary>
    private async Task<string[]> tenantsHoldingAsync(Guid articleId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT tenant_id FROM pgvector_conjoined_proj.article_vectors WHERE id = $1 ORDER BY tenant_id";
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = articleId });

        var tenants = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tenants.Add(reader.GetString(0));
        }

        return tenants.ToArray();
    }

    /// <summary>
    ///     marten#5420 failure 3: the cross-tenant delete.
    /// </summary>
    [Fact]
    public async Task a_delete_in_one_tenant_leaves_the_other_tenants_row()
    {
        var articleId = Guid.NewGuid();

        await writeAsync("tenant_a", articleId, "the fox in the snow");
        await writeAsync("tenant_b", articleId, "a heron at dusk");

        await using (var session = _store.LightweightSession("tenant_b"))
        {
            session.Events.Append(articleId, new ArticleRetracted(articleId));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await searchAsync("tenant_b", "a heron at dusk")).ShouldBeEmpty();
        (await searchAsync("tenant_a", "the fox in the snow")).Single().ContentText
            .ShouldBe("the fox in the snow");
    }
}

/// <summary>
///     The registration-side guard: a conjoined store that builds the table the old way is refused when
///     the store is built, rather than leaking silently forever.
/// </summary>
[Collection("Marten.PgVector")]
public class conjoined_vector_projection_validation_tests
{
    [Fact]
    public void a_conjoined_store_refuses_a_table_built_without_tenant_id()
    {
        var projection = new ArticleVectorProjection(new FakeEmbeddingProvider(dimensions: 3));

        var ex = Should.Throw<Exception>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "pgvector_conjoined_invalid";
                opts.UsePgVector();
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;

                opts.Projections.Add(projection, ProjectionLifecycle.Inline);

                // The pre-marten#5420 spelling: no tenancy, so no tenant_id column.
                opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_conjoined_invalid"));
            });
        });

        ex.Message.ShouldContain("tenant_id");
        ex.Message.ShouldContain("BuildTable(opts)");
    }

    /// <summary>
    ///     A single-tenant store is untouched: the old spelling stays correct and stays silent, which
    ///     is what every existing consumer is doing.
    /// </summary>
    [Fact]
    public void a_single_tenant_store_still_accepts_the_schema_name_overload()
    {
        var projection = new ArticleVectorProjection(new FakeEmbeddingProvider(dimensions: 3));

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_single_ok";
            opts.UsePgVector();

            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_single_ok"));
        });

        store.ShouldNotBeNull();
    }
}
