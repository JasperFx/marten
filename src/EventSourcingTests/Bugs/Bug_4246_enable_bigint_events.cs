using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Tests for the EnableBigIntEvents flag that controls whether
/// mt_quick_append_events uses bigint (64-bit) types for version,
/// sequence, and return values. Without this flag, sequence values
/// exceeding int32 range (~2.1B) cause integer out of range errors.
/// </summary>
public class Bug_4246_enable_bigint_events : OneOffConfigurationsContext
{
    [Fact]
    public async Task events_work_normally_with_bigint_disabled()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            // EnableBigIntEvents is false by default
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Quest 1" },
                new MembersJoined { Members = new[] { "Frodo", "Sam" } });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new MembersDeparted { Members = new[] { "Sam" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var events = await query.Events.FetchStreamAsync(streamId);
            events.Count.ShouldBe(3);
            events[0].Version.ShouldBe(1);
            events[1].Version.ShouldBe(2);
            events[2].Version.ShouldBe(3);
        }
    }

    [Fact]
    public async Task events_work_normally_with_bigint_enabled()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.EnableBigIntEvents = true;
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Quest 2" },
                new MembersJoined { Members = new[] { "Aragorn", "Legolas" } });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new MembersDeparted { Members = new[] { "Legolas" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var events = await query.Events.FetchStreamAsync(streamId);
            events.Count.ShouldBe(3);
            events[0].Version.ShouldBe(1);
            events[1].Version.ShouldBe(2);
            events[2].Version.ShouldBe(3);
        }
    }

    [Fact]
    public async Task bigint_enabled_handles_sequences_above_int32_max()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.EnableBigIntEvents = true;
        });

        var schemaName = theStore.Options.DatabaseSchemaName;
        const long largeSequence = 2_200_000_000L;

        // Append first event at normal sequence
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "BigInt Test" });
            await session.SaveChangesAsync();
        }

        // Jump the sequence past int32 max
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var restartCmd = conn.CreateCommand();
        restartCmd.CommandText = $"ALTER SEQUENCE {schemaName}.mt_events_sequence RESTART WITH {largeSequence}";
        await restartCmd.ExecuteNonQueryAsync();
        await conn.CloseAsync();

        // Append second event — this would fail with int overflow without bigint
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new MembersJoined { Members = new[] { "Gandalf" } });
            await session.SaveChangesAsync();
        }

        // Verify both events exist with correct sequences
        await using (var query = theStore.QuerySession())
        {
            var events = await query.Events.FetchStreamAsync(streamId);
            events.Count.ShouldBe(2);
            events[0].Sequence.ShouldBe(1L);
            events[1].Sequence.ShouldBe(largeSequence);
        }
    }

    [Fact]
    public void bigint_events_is_false_by_default()
    {
        var opts = new StoreOptions();
        opts.Events.EnableBigIntEvents.ShouldBeFalse();
    }

    [Fact]
    public async Task function_uses_int_when_flag_is_false()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            // EnableBigIntEvents defaults to false
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);

        // Check the function DDL contains int types
        var ddl = theStore.Storage.Database.ToDatabaseScript();
        // The function should use "int" not "bigint" when flag is off
        // (DDL only shows pending changes, so verify via function definition)
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        var schema = theStore.Options.DatabaseSchemaName;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_get_functiondef(oid) FROM pg_proc WHERE proname = 'mt_quick_append_events' AND pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = '{schema}')";
        var funcDef = (string?)await cmd.ExecuteScalarAsync();

        funcDef.ShouldNotBeNull();
        funcDef.ShouldContain("integer[]"); // Returns int[] when bigint disabled
    }

    [Fact]
    public async Task function_uses_bigint_when_flag_is_true()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.EnableBigIntEvents = true;
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        var schema = theStore.Options.DatabaseSchemaName;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT pg_get_functiondef(oid) FROM pg_proc WHERE proname = 'mt_quick_append_events' AND pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = '{schema}')";
        var funcDef = (string?)await cmd.ExecuteScalarAsync();

        funcDef.ShouldNotBeNull();
        funcDef.ShouldContain("bigint[]"); // Returns bigint[] when enabled
    }
}
