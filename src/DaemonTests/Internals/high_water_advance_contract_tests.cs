#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Internals;

/// <summary>
///     #5738 / jasperfx#1031: the store-agnostic <see cref="IEventStore.PreviewHighWaterAdvanceAsync" /> and
///     <see cref="IEventStore.AdvanceHighWaterMarkToLatestAsync" />, reached through <see cref="IEventStore" />
///     the way CritterWatch reaches them, on a database that keeps one store-global mark.
/// </summary>
public class high_water_advance_contract_tests: OneOffConfigurationsContext
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IEventStore eventStore => theStore;

    private async Task<IEventDatabase> databaseAsync() => (await eventStore.AllDatabases()).Single();

    /// <summary>
    ///     Three appends with the middle one removed, so one sequence number between them has no committed
    ///     event, and the stored mark set to the first append.
    /// </summary>
    private async Task<(long Floor, long Max)> appendWithAHoleAsync()
    {
        var before = await startStreamAsync("Before");
        var hole = await startStreamAsync("Rolled back");
        await startStreamAsync("After");
        await executeAsync($"delete from {SchemaName}.mt_events where stream_id = '{hole}'");

        var floor = await seqIdOfAsync(before);
        await executeAsync(
            $"insert into {SchemaName}.mt_event_progression (name, last_seq_id) values ('HighWaterMark', {floor})");

        return (floor, await maxSeqIdAsync());
    }

    [Fact]
    public async Task the_preview_reports_the_mark_the_ceiling_and_the_hole_and_writes_nothing()
    {
        var (floor, max) = await appendWithAHoleAsync();

        var preview = (await eventStore.PreviewHighWaterAdvanceAsync(await databaseAsync(), null, Token)).Single();

        preview.DatabaseIdentifier.ShouldBe(theStore.Storage.Database.Identifier);
        preview.TenantId.ShouldBeNull();
        preview.CurrentMark.ShouldBe(floor);
        preview.CommittedCeiling.ShouldBe(max);
        preview.MissingSequenceCount.ShouldBe(1);
        preview.WouldAdvance.ShouldBeTrue();

        (await readMarkAsync("HighWaterMark")).ShouldBe(floor);
    }

    [Fact]
    public async Task an_advance_against_the_previewed_mark_moves_it_and_counts_what_it_skipped()
    {
        var (floor, max) = await appendWithAHoleAsync();
        var database = await databaseAsync();
        var preview = (await eventStore.PreviewHighWaterAdvanceAsync(database, null, Token)).Single();

        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(database, null, preview.CurrentMark, Token))
            .Single();

        advance.DatabaseIdentifier.ShouldBe(theStore.Storage.Database.Identifier);
        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);
        advance.PreviousMark.ShouldBe(floor);
        advance.NewMark.ShouldBe(max);
        advance.SkippedSequenceCount.ShouldBe(1);
        (await readMarkAsync("HighWaterMark")).ShouldBe(max);
    }

    [Fact]
    public async Task a_stale_expectation_is_refused_and_writes_nothing()
    {
        var (floor, max) = await appendWithAHoleAsync();

        // The operator looked at an older mark; the daemon has since moved it on by itself
        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(await databaseAsync(), null, floor - 1,
            Token)).Single();

        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.RefusedStaleExpectation);
        advance.PreviousMark.ShouldBe(floor);
        advance.NewMark.ShouldBe(floor);
        advance.SkippedSequenceCount.ShouldBe(0);
        (await readMarkAsync("HighWaterMark")).ShouldBe(floor);
        max.ShouldBeGreaterThan(floor);
    }

    [Fact]
    public async Task a_mark_at_the_ceiling_is_left_alone_and_never_moves_backwards()
    {
        var (_, max) = await appendWithAHoleAsync();
        await executeAsync(
            $"update {SchemaName}.mt_event_progression set last_seq_id = {max + 10} where name = 'HighWaterMark'");

        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(await databaseAsync(), null, null, Token))
            .Single();

        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.AlreadyAtCeiling);
        advance.PreviousMark.ShouldBe(max + 10);
        advance.NewMark.ShouldBe(max + 10);
        (await readMarkAsync("HighWaterMark")).ShouldBe(max + 10);
    }

    [Fact]
    public async Task a_mark_never_stored_counts_as_zero_and_the_advance_writes_its_first_row()
    {
        await startStreamAsync("One");
        await startStreamAsync("Two");
        var database = await databaseAsync();

        var preview = (await eventStore.PreviewHighWaterAdvanceAsync(database, null, Token)).Single();
        preview.CurrentMark.ShouldBe(0);
        preview.MissingSequenceCount.ShouldBe(0);
        (await readMarkAsync("HighWaterMark")).ShouldBeNull();

        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(database, null, 0, Token)).Single();

        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.Advanced);
        advance.PreviousMark.ShouldBe(0);
        advance.NewMark.ShouldBe(await maxSeqIdAsync());
        advance.SkippedSequenceCount.ShouldBe(0);
        (await readMarkAsync("HighWaterMark")).ShouldBe(await maxSeqIdAsync());
    }

    [Fact]
    public async Task no_events_is_already_at_the_ceiling_and_writes_nothing()
    {
        var advance = (await eventStore.AdvanceHighWaterMarkToLatestAsync(await databaseAsync(), null, null, Token))
            .Single();

        advance.Outcome.ShouldBe(HighWaterAdvanceOutcome.AlreadyAtCeiling);
        advance.NewMark.ShouldBe(0);
        (await readMarkAsync("HighWaterMark")).ShouldBeNull();
    }

    [Fact]
    public async Task a_named_tenant_is_refused_where_the_database_keeps_one_mark()
    {
        var (floor, _) = await appendWithAHoleAsync();
        var database = await databaseAsync();

        await Should.ThrowAsync<ArgumentException>(
            () => eventStore.PreviewHighWaterAdvanceAsync(database, "red", Token));
        await Should.ThrowAsync<ArgumentException>(
            () => eventStore.AdvanceHighWaterMarkToLatestAsync(database, "red", null, Token));

        (await readMarkAsync("HighWaterMark")).ShouldBe(floor);
    }

    [Fact]
    public async Task the_advanced_overload_still_advances_the_database_mark()
    {
        var (_, max) = await appendWithAHoleAsync();

        await theStore.Advanced.AdvanceHighWaterMarkToLatestAsync(Token);

        (await readMarkAsync("HighWaterMark")).ShouldBe(max);
    }

    private async Task<Guid> startStreamAsync(string name)
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(id, new HighWaterAdvanceStarted(name));
        await session.SaveChangesAsync(Token);
        return id;
    }

    private async Task<long> seqIdOfAsync(Guid streamId)
    {
        await using var conn = await openAsync();
        await using var cmd = conn.CreateCommand($"select max(seq_id) from {SchemaName}.mt_events where stream_id = :id");
        cmd.Parameters.AddWithValue("id", streamId);
        return (long)(await cmd.ExecuteScalarAsync(Token))!;
    }

    private async Task<long> maxSeqIdAsync()
    {
        await using var conn = await openAsync();
        return (long)(await conn.CreateCommand($"select max(seq_id) from {SchemaName}.mt_events")
            .ExecuteScalarAsync(Token))!;
    }

    private async Task<long?> readMarkAsync(string name)
    {
        await using var conn = await openAsync();
        await using var cmd =
            conn.CreateCommand($"select last_seq_id from {SchemaName}.mt_event_progression where name = :name");
        cmd.Parameters.AddWithValue("name", name);
        return await cmd.ExecuteScalarAsync(Token) as long?;
    }

    private async Task executeAsync(string sql)
    {
        await using var conn = await openAsync();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync(Token);
    }

    private async Task<NpgsqlConnection> openAsync()
    {
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), Token);
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(Token);
        return conn;
    }
}

public record HighWaterAdvanceStarted(string Name);
