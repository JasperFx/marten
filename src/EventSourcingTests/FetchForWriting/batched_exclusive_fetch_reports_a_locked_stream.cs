using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

// The assertions are on Execute(): when it throws, the enlisted items are never completed.
public class batched_exclusive_fetch_reports_a_locked_stream: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    [InlineData(ProjectionLifecycle.Live)]
    public async Task an_exclusive_fetch_of_a_locked_stream_throws_stream_locked(ProjectionLifecycle lifecycle)
    {
        var streamId = await startLockableStream(lifecycle);

        await using var holder = theStore.LightweightSession();
        await holder.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        await using var other = theStore.LightweightSession();
        var batch = other.CreateBatchQuery();
        _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        var ex = await Should.ThrowAsync<StreamLockedException>(() => batch.Execute());
        ex.Message.ShouldContain(streamId.ToString());
    }

    [Fact]
    public async Task an_exclusive_fetch_after_another_item_throws_stream_locked()
    {
        var streamId = await startLockableStream(ProjectionLifecycle.Inline);

        await using var holder = theStore.LightweightSession();
        await holder.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        await using var other = theStore.LightweightSession();
        var batch = other.CreateBatchQuery();
        _ = batch.Load<SimpleAggregate>(streamId);
        _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        var ex = await Should.ThrowAsync<StreamLockedException>(() => batch.Execute());
        ex.Message.ShouldContain(streamId.ToString());
    }

    [Fact]
    public async Task a_slow_query_in_the_same_batch_stays_a_command_exception()
    {
        var streamId = await startLockableStream(ProjectionLifecycle.Inline);

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        _ = batch.Query<SimpleAggregate>("select pg_sleep(3)");
        _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        await Should.ThrowAsync<MartenCommandException>(() => batch.Execute());
    }

    private async Task<Guid> startLockableStream(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.CommandTimeout = 1;
            if (lifecycle == ProjectionLifecycle.Inline) opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
            if (lifecycle == ProjectionLifecycle.Async) opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();
        return streamId;
    }
}
