#nullable enable
#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Cold;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class marking_many_streams_cold: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();
    private readonly SqlRecorder _sql = new();

    private void configure(Action<StoreOptions>? more = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            more?.Invoke(opts);
        });
    }

    private async Task<string> startCart(int items)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key,
            new object[] { new CartOpened("anne") }.Concat(Enumerable.Range(1, items).Select(i => new ItemAdded($"item {i}"))).ToArray());
        await session.SaveChangesAsync();
        return key;
    }

    private async Task markCold(params (string Key, long Version)[] streams)
    {
        await using var session = theStore.LightweightSession();
        session.Logger = _sql;
        session.Events.MarkStreamsCold(streams);
        await session.SaveChangesAsync();
    }

    private async Task<long[]> warmVersions(string key)
    {
        await using var session = theStore.LightweightSession();
        return (await session.Events.FetchStreamAsync(key)).Select(x => x.Version).ToArray();
    }

    [Fact]
    public async Task each_stream_moves_through_its_own_version_in_one_statement()
    {
        configure();
        var one = await startCart(1);
        var two = await startCart(3);
        var three = await startCart(2);

        await markCold((one, 2), (two, 3), (three, 1));

        _sql.Statements.Count(x => x.Contains("mt_events")).ShouldBe(1);
        (await warmVersions(one)).ShouldBeEmpty();
        (await warmVersions(two)).ShouldBe([4L]);
        (await warmVersions(three)).ShouldBe([2L, 3L]);
    }

    [Fact]
    public async Task a_stream_already_cold_at_a_higher_version_keeps_its_boundary()
    {
        configure();
        var colder = await startCart(3);
        var other = await startCart(3);
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, colder, 3);

        await markCold((colder, 2), (other, 2));

        (await warmVersions(colder)).ShouldBe([4L]);
        (await warmVersions(other)).ShouldBe([3L, 4L]);

        while (_cold.ReadThrough.TryDequeue(out _)) { }
        await using var session = theStore.LightweightSession();
        (await session.Events.FetchLatest<Cart>(colder))!.Items.ShouldBe(["item 1", "item 2", "item 3"]);
        _cold.ReadThrough.ShouldBe([3L]);
    }

    [Fact]
    public async Task a_stream_named_twice_moves_through_the_higher_version()
    {
        configure();
        var key = await startCart(3);

        await markCold((key, 3), (key, 2));

        (await warmVersions(key)).ShouldBe([4L]);
    }

    [Fact]
    public async Task one_stream_that_has_not_reached_its_version_fails_the_whole_batch()
    {
        configure();
        var fine = await startCart(1);
        var shortOne = await startCart(1);
        var missing = Guid.NewGuid().ToString();

        var e = await Should.ThrowAsync<Exception>(() => markCold((fine, 2), (shortOne, 5), (missing, 1)));
        e.ToString().ShouldContain($"{shortOne} through 5");
        e.ToString().ShouldContain($"{missing} through 1");
        e.ToString().ShouldNotContain($"{fine} through");

        (await warmVersions(fine)).ShouldBe([1L, 2L]);
        (await warmVersions(shortOne)).ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task an_async_shard_behind_on_one_stream_blocks_the_whole_batch()
    {
        configure(opts => opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Async));

        var processed = await startCart(1);
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
        }

        var notYetProcessed = await startCart(1);

        var e = await Should.ThrowAsync<InvalidOperationException>(() => markCold((processed, 2), (notYetProcessed, 2)));
        e.Message.ShouldContain(nameof(CartItemCount));
        _sql.Statements.Count(x => x.Contains("lagging as")).ShouldBe(1);

        (await warmVersions(processed)).ShouldBe([1L, 2L]);
        (await warmVersions(notYetProcessed)).ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task marks_queued_one_after_the_other_share_one_statement()
    {
        configure();
        var one = await startCart(2);
        var two = await startCart(2);
        var three = await startCart(2);

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = _sql;
            session.Events.MarkStreamCold(one, 2);
            session.Events.MarkStreamCold(two, 3);
            session.Events.MarkStreamsCold([(three, 1)]);
            await session.SaveChangesAsync();
        }

        _sql.Statements.Count(x => x.Contains("lagging as")).ShouldBe(1);
        (await warmVersions(one)).ShouldBe([3L]);
        (await warmVersions(two)).ShouldBeEmpty();
        (await warmVersions(three)).ShouldBe([2L, 3L]);
    }

    [Fact]
    public async Task an_empty_batch_does_nothing()
    {
        configure();

        await markCold();

        _sql.Statements.ShouldBeEmpty();
    }

    private class SqlRecorder: IMartenSessionLogger
    {
        public List<string> Statements { get; } = new();

        public void OnBeforeExecute(NpgsqlCommand command) => Statements.Add(command.CommandText);

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            foreach (var command in batch.BatchCommands) Statements.Add(command.CommandText);
        }

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }
}
