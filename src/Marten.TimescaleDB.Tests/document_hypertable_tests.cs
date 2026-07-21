using Marten.Testing.Harness;
using Marten.TimescaleDB;
using Shouldly;
using Xunit;

namespace Marten.TimescaleDB.Tests;

public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public int Severity { get; set; }
}

[Collection("timescaledb")]
public class document_hypertable_tests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "timescale_doc";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UseTimescaleDB(ts =>
            {
                ts.DocumentAsHypertable<AuditEntry>(x => x.CreatedAt, hyper =>
                {
                    hyper.ChunkInterval = TimeSpan.FromDays(1);
                    hyper.CompressAfter = TimeSpan.FromDays(30);
                });
            });
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    [Fact]
    public async Task document_table_is_a_hypertable()
    {
        var count = await ScalarAsync<long>(
            "select count(*) from timescaledb_information.hypertables where hypertable_schema = 'timescale_doc' and hypertable_name = 'mt_doc_auditentry'");
        count.ShouldBe(1);
    }

    [Fact]
    public async Task created_at_is_part_of_the_primary_key()
    {
        // The duplicated created_at column must be NOT NULL and part of the composite PK (id, created_at).
        var pkCols = await ScalarAsync<long>(
            @"select count(*) from information_schema.key_column_usage k
              join information_schema.table_constraints c
                on k.constraint_name = c.constraint_name and k.table_schema = c.table_schema
              where c.constraint_type = 'PRIMARY KEY'
                and k.table_schema = 'timescale_doc' and k.table_name = 'mt_doc_auditentry'
                and k.column_name = 'created_at'");
        pkCols.ShouldBe(1);
    }

    [Fact]
    public async Task database_matches_configuration_after_conversion()
    {
        await Should.NotThrowAsync(async () =>
            await _store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task full_document_lifecycle_store_load_update_delete()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 15, 8, 30, 0, TimeSpan.Zero);

        // Store
        await using (var session = _store.LightweightSession())
        {
            session.Store(new AuditEntry { Id = id, CreatedAt = createdAt, Action = "created", Severity = 1 });
            await session.SaveChangesAsync();
        }

        // Load by id (composite PK, but created_at is immutable so exactly one row per id)
        await using (var session = _store.QuerySession())
        {
            var loaded = await session.LoadAsync<AuditEntry>(id);
            loaded.ShouldNotBeNull();
            loaded.Action.ShouldBe("created");
        }

        // Update (created_at unchanged)
        await using (var session = _store.LightweightSession())
        {
            var loaded = await session.LoadAsync<AuditEntry>(id);
            loaded!.Action = "updated";
            loaded.Severity = 5;
            session.Store(loaded);
            await session.SaveChangesAsync();
        }

        await using (var session = _store.QuerySession())
        {
            var loaded = await session.LoadAsync<AuditEntry>(id);
            loaded!.Action.ShouldBe("updated");
            loaded.Severity.ShouldBe(5);
        }

        // Exactly one row (no duplicate from the update)
        var rows = await ScalarAsync<long>("select count(*) from timescale_doc.mt_doc_auditentry");
        rows.ShouldBe(1);

        // Delete
        await using (var session = _store.LightweightSession())
        {
            session.Delete<AuditEntry>(id);
            await session.SaveChangesAsync();
        }

        await using (var session = _store.QuerySession())
        {
            (await session.LoadAsync<AuditEntry>(id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task linq_query_over_hypertable_documents()
    {
        await using (var session = _store.LightweightSession())
        {
            for (var i = 0; i < 10; i++)
            {
                session.Store(new AuditEntry
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
                    Action = "event",
                    Severity = i
                });
            }

            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var high = await query.Query<AuditEntry>().CountAsync(x => x.Severity >= 5);
        high.ShouldBe(5);
    }
}
