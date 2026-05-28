using Marten.PgVector;
using Marten.PgVector.Tests.SingleTenancy;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Pgvector;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace Marten.PgVector.Tests.MultiDatabase;

/// <summary>
/// Tests that verify pgvector works with database-per-tenant multi-tenancy.
/// Also serves as verification for issue #2515 — PostgreSQL extensions must be
/// created in EACH tenant database, not just the default.
/// </summary>
[Collection("Marten.PgVector")]
public class database_per_tenant_vector_tests : IAsyncLifetime
{
    private static readonly string[] TenantDatabases = { "pgvector_tenant1", "pgvector_tenant2", "pgvector_tenant3" };

    private DocumentStore _store = null!;
    private readonly Dictionary<string, string> _tenantConnStrs = new();

    public async Task InitializeAsync()
    {
        // Create the per-tenant databases if they don't exist and drop any stale schema
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();

            foreach (var db in TenantDatabases)
            {
                _tenantConnStrs[db] = await CreateDatabaseIfNotExists(conn, db);
            }
        }

        _store = DocumentStore.For(opts =>
        {
            opts.DatabaseSchemaName = "pgvector_mt";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePgVector();
            opts.RegisterDocumentType<ProductWithVector>();

            // Configure database-per-tenant using SingleServerMultiTenancy
            opts.MultiTenantedDatabases(x =>
            {
                foreach (var db in TenantDatabases)
                {
                    x.AddSingleTenantDatabase(_tenantConnStrs[db], db);
                }
            });
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;
        var connectionString = builder.ConnectionString;

        // Wipe any prior pgvector_mt schema so the test runs clean
        await SchemaUtils.DropSchema(connectionString, "pgvector_mt");

        return connectionString;
    }

    /// <summary>
    /// Issue #2515 verification: the pgvector extension must be created in each tenant database.
    /// </summary>
    [Fact]
    public async Task vector_extension_is_created_in_each_tenant_database()
    {
        foreach (var db in TenantDatabases)
        {
            await using var conn = new NpgsqlConnection(_tenantConnStrs[db]);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM pg_extension WHERE extname = 'vector'";
            var result = await cmd.ExecuteScalarAsync();

            result.ShouldNotBeNull(
                $"pgvector extension was NOT created in database '{db}'. " +
                "This is the core issue from #2515 — extensions must be created in all tenant databases.");
        }
    }

    [Fact]
    public async Task document_tables_exist_in_each_tenant_database()
    {
        foreach (var db in TenantDatabases)
        {
            await using var conn = new NpgsqlConnection(_tenantConnStrs[db]);
            await conn.OpenAsync();

            var tables = await conn.ExistingTablesAsync();
            tables.Any(t => t.Name.Contains("mt_doc_productwithvector"))
                .ShouldBeTrue($"Document table not found in database '{db}'");
        }
    }

    [Fact]
    public async Task can_store_and_load_documents_per_tenant()
    {
        // Store in tenant1
        await using (var session = _store.LightweightSession(TenantDatabases[0]))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "T1 Product",
                Embedding = new float[] { 1, 0, 0 }
            });
            await session.SaveChangesAsync();
        }

        // Store in tenant2
        await using (var session = _store.LightweightSession(TenantDatabases[1]))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "T2 Product",
                Embedding = new float[] { 0, 1, 0 }
            });
            await session.SaveChangesAsync();
        }

        // Query tenant1
        await using (var q = _store.QuerySession(TenantDatabases[0]))
        {
            var results = await q.Query<ProductWithVector>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].Name.ShouldBe("T1 Product");
        }

        // Query tenant2
        await using (var q = _store.QuerySession(TenantDatabases[1]))
        {
            var results = await q.Query<ProductWithVector>().ToListAsync();
            results.Count.ShouldBe(1);
            results[0].Name.ShouldBe("T2 Product");
        }

        // Tenant3 should be empty
        await using (var q = _store.QuerySession(TenantDatabases[2]))
        {
            var results = await q.Query<ProductWithVector>().ToListAsync();
            results.Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task vector_search_works_per_tenant_database()
    {
        // Store vectors in different tenant databases
        await using (var session = _store.LightweightSession(TenantDatabases[0]))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "T1-Near",
                Embedding = new float[] { 0.9f, 0.1f, 0 }
            });
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "T1-Far",
                Embedding = new float[] { 0, 0, 1 }
            });
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession(TenantDatabases[1]))
        {
            session.Store(new ProductWithVector
            {
                Id = Guid.NewGuid(), Name = "T2-Near",
                Embedding = new float[] { 0.95f, 0.05f, 0 }
            });
            await session.SaveChangesAsync();
        }

        var queryVector = new Vector(new float[] { 1, 0, 0 });

        // Search tenant1 — should only find tenant1's documents
        await using var q1 = _store.QuerySession(TenantDatabases[0]);
        var results1 = await q1.VectorSearchAsync<ProductWithVector>(
            x => x.Embedding, queryVector, limit: 10, distance: DistanceFunction.L2);

        results1.Count.ShouldBe(2);
        results1.ShouldAllBe(r => r.Name.StartsWith("T1-"));
        results1[0].Name.ShouldBe("T1-Near"); // closest first

        // Search tenant2 — should only find tenant2's documents
        await using var q2 = _store.QuerySession(TenantDatabases[1]);
        var results2 = await q2.VectorSearchAsync<ProductWithVector>(
            x => x.Embedding, queryVector, limit: 10, distance: DistanceFunction.L2);

        results2.Count.ShouldBe(1);
        results2[0].Name.ShouldBe("T2-Near");
    }
}
