using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Projections.Flattened;

/// <summary>
/// Regression test for https://github.com/JasperFx/marten/issues/4255.
///
/// When a FlatTableProjection maps multiple events to the same table, and the table
/// has a NOT NULL column that is not populated by every event, partial-mapping events
/// previously produced an INSERT … ON CONFLICT DO UPDATE that violated the NOT NULL
/// constraint.
///
/// The fix: partial-mapping events now generate an UPDATE-only function. Full-mapping
/// events keep the original INSERT … ON CONFLICT DO UPDATE behavior.
/// </summary>
public class Bug_4255_flat_table_not_null_constraint : OneOffConfigurationsContext
{
    [Fact]
    public async Task partial_event_on_existing_row_updates_without_violating_not_null()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Bug4255Projection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

        // Add a NOT NULL constraint on other_id, simulating the scenario where a user
        // has created the table out-of-band with stricter constraints than Marten infers.
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {SchemaName}.bug_4255_proj ALTER COLUMN other_id SET NOT NULL;";
            await cmd.ExecuteNonQueryAsync();
        }

        var streamId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        // EventA maps every non-PK column (full-mapping) — creates the row via INSERT ON CONFLICT
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Bug4255EventA(streamId, otherId, "initial"));
            await session.SaveChangesAsync();
        }

        // EventB maps only `field` (partial-mapping) — after fix, UPDATE-only so
        // the NOT NULL constraint is not violated.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new Bug4255EventB("changed"));
            await session.SaveChangesAsync();
        }

        // Verify the UPDATE happened: field changed, other_id preserved.
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT other_id, field FROM {SchemaName}.bug_4255_proj WHERE id = @id";
            cmd.AddNamedParameter("id", streamId);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).ShouldBeTrue();
            reader.GetGuid(0).ShouldBe(otherId);
            reader.GetString(1).ShouldBe("changed");
        }
    }

    [Fact]
    public async Task partial_event_on_new_stream_is_a_safe_noop()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Bug4255Projection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {SchemaName}.bug_4255_proj ALTER COLUMN other_id SET NOT NULL;";
            await cmd.ExecuteNonQueryAsync();
        }

        var newStreamId = Guid.NewGuid();

        // Starting a new stream with a partial event: no row exists yet.
        // Previously, this threw a NOT NULL violation. After the fix, the UPDATE
        // statement matches zero rows and is a no-op.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(newStreamId, new Bug4255EventB("first-is-b"));
            await session.SaveChangesAsync();
        }

        // No row should be created because partial events are UPDATE-only.
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {SchemaName}.bug_4255_proj WHERE id = @id";
            cmd.AddNamedParameter("id", newStreamId);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.ShouldBe(0L);
        }
    }

    [Fact]
    public async Task full_mapping_event_still_uses_insert_on_conflict()
    {
        // Sanity check: the existing INSERT … ON CONFLICT DO UPDATE path is preserved
        // for events that map every non-PK column.
        StoreOptions(opts =>
        {
            opts.Projections.Add<Bug4255Projection>(ProjectionLifecycle.Inline);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

        var streamId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Bug4255EventA(streamId, otherId, "hello"));
            await session.SaveChangesAsync();
        }

        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT other_id, field FROM {SchemaName}.bug_4255_proj WHERE id = @id";
            cmd.AddNamedParameter("id", streamId);
            await using var reader = await cmd.ExecuteReaderAsync();

            (await reader.ReadAsync()).ShouldBeTrue();
            reader.GetGuid(0).ShouldBe(otherId);
            reader.GetString(1).ShouldBe("hello");
        }
    }
}

public class Bug4255Projection : FlatTableProjection
{
    public Bug4255Projection() : base("bug_4255_proj", SchemaNameSource.DocumentSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<Guid>("other_id");
        Table.AddColumn<string>("field");

        // EventA populates every non-PK column — full mapping, INSERT ON CONFLICT
        Project<Bug4255EventA>(map =>
        {
            map.Map(e => e.OtherId, "other_id");
            map.Map(e => e.Field, "field");
        }, e => e.Id);

        // EventB only populates `field` — partial mapping, UPDATE-only after fix
        Project<Bug4255EventB>(map => { map.Map(e => e.Field, "field"); });
    }
}

public record Bug4255EventA(Guid Id, Guid OtherId, string Field);
public record Bug4255EventB(string Field);
