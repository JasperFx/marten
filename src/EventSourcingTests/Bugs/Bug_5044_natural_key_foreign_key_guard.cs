using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #5044. mt_natural_key_X guards its foreign key creation with a DO $$ IF NOT EXISTS block, but the
/// guard tested `pg_constraint.conname` alone. conname is only unique per relation, and the FK name
/// is derived from the aggregate type name, so any second Marten store using the same aggregate in a
/// different schema of the same database saw the FIRST store's constraint, skipped creating its own,
/// and was then permanently out of sync with its configuration.
/// </summary>
public class Bug_5044_natural_key_foreign_key_guard: IAsyncLifetime
{
    private readonly string _schemaOne = $"nk5044_one_{Guid.NewGuid():N}".Substring(0, 26);
    private readonly string _schemaTwo = $"nk5044_two_{Guid.NewGuid():N}".Substring(0, 26);

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        foreach (var schema in new[] { _schemaOne, _schemaTwo })
        {
            try
            {
                await conn.DropSchemaAsync(schema);
            }
            catch
            {
                // nothing to clean up
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static DocumentStore StoreFor(string schema)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Projections.Snapshot<Widget>(SnapshotLifecycle.Inline);
        });
    }

    [Fact]
    public async Task two_schemas_in_one_database_each_get_their_own_foreign_key()
    {
        await using (var first = StoreFor(_schemaOne))
        {
            await first.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        await using (var second = StoreFor(_schemaTwo))
        {
            await second.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        foreach (var schema in new[] { _schemaOne, _schemaTwo })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
select count(*) from pg_constraint con
join pg_class rel on rel.oid = con.conrelid
join pg_namespace nsp on nsp.oid = rel.relnamespace
where nsp.nspname = @schema and rel.relname = 'mt_natural_key_widget' and con.contype = 'f'";
            cmd.Parameters.AddWithValue("schema", schema);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.ShouldBe(1, $"schema {schema} should have its own foreign key on mt_natural_key_widget");
        }

        // ...and neither store reads as drift afterwards.
        await using (var first = StoreFor(_schemaOne))
        {
            await first.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }

        await using (var second = StoreFor(_schemaTwo))
        {
            await second.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        }
    }
}

public sealed record WidgetCode(string Value);

public sealed record WidgetRegistered(Guid WidgetId, string Code);

public sealed record Widget
{
    public Guid Id { get; set; }

    [NaturalKey]
    public WidgetCode Code { get; set; } = null!;

    [NaturalKeySource]
    public static Widget Create(WidgetRegistered e) => new() { Id = e.WidgetId, Code = new WidgetCode(e.Code) };
}
