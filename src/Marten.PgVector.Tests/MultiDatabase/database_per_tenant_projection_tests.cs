using JasperFx;
using JasperFx.Events.Projections;
using Marten.PgVector;
using Marten.PgVector.Tests.Helpers;
using Marten.PgVector.Projection;
using Marten.PgVector.Tests.SingleTenancy;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;
using System.Threading.Tasks;

namespace Marten.PgVector.Tests.MultiDatabase;

/// <summary>
///     A vector projection has to write into the database its events came from.
/// </summary>
/// <remarks>
///     <para>
///         <c>VectorProjection</c> resolved its connection from <c>store.Storage.Database</c>, whose own
///         doc comment says it is "the default database when *not* using database per tenant
///         multi-tenancy". So every tenant's embeddings were written to one database while
///         <c>VectorProjectionSearchAsync</c> read from the session's — the two disagreed, and a search
///         against the tenant that raised the events found nothing at all.
///     </para>
///     <para>
///         Nothing in the existing multi-database suite could catch it: those tests exercise the
///         document path (<c>VectorSearchAsync</c>), which was always session-scoped. The projection
///         had no multi-tenanted coverage of any kind.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class database_per_tenant_projection_tests: IAsyncLifetime
{
    private static readonly string[] TenantDatabases = ["pgvector_proj_t1", "pgvector_proj_t2"];

    private readonly Dictionary<string, string> _tenantConnStrs = new();
    private FakeEmbeddingProvider _embedder = null!;
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            foreach (var db in TenantDatabases)
            {
                _tenantConnStrs[db] = await CreateDatabaseIfNotExists(conn, db);
            }
        }

        _embedder = new FakeEmbeddingProvider(3);
        var projection = new ArticleSearchProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.DatabaseSchemaName = "pgvector_proj_mt";
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UsePgVector();
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_proj_mt"));

            opts.Events.AddEventType<ArticleWritten>();
            opts.Events.AddEventType<ArticleRetracted>();

            opts.MultiTenantedDatabases(x =>
            {
                foreach (var db in TenantDatabases)
                {
                    x.AddSingleTenantDatabase(_tenantConnStrs[db], db);
                }
            });
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Nothing clears these databases between runs, and every run appends the same content under a
        // new id — so without this the search below legitimately finds more than one row.
        foreach (var connectionString in _tenantConnStrs.Values)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "truncate table pgvector_proj_mt.article_search_vectors";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private static async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        if (!await conn.DatabaseExists(databaseName))
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    [Fact]
    public async Task the_projection_writes_into_the_tenants_own_database()
    {
        var articleId = Guid.NewGuid();

        await using (var session = _store.LightweightSession("pgvector_proj_t2"))
        {
            session.Events.StartStream(Guid.NewGuid(), new ArticleWritten(articleId, "a tenanted article"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Without the fix the row went to the store's default database, so this finds nothing.
        await using (var query = _store.QuerySession("pgvector_proj_t2"))
        {
            var found = await query.VectorProjectionSearchAsync("article_search_vectors",
                _embedder.GenerateVector("a tenanted article"), 10, DistanceFunction.L2);

            found.Single().Id.ShouldBe(articleId);
        }

        // And the other tenant's database must not have it.
        await using (var query = _store.QuerySession("pgvector_proj_t1"))
        {
            var found = await query.VectorProjectionSearchAsync("article_search_vectors",
                _embedder.GenerateVector("a tenanted article"), 10, DistanceFunction.L2);

            found.ShouldBeEmpty();
        }
    }
}
