using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Protected;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

/// <summary>
/// Compaction replaces the last event with a <see cref="Compacted{T}"/> snapshot and deletes the rest, so
/// every way out of it that leaves the history unrecoverable has to be a deliberate one. These pin the two
/// that were not: a stream folding to no aggregate, and an archiver the store could not call.
/// </summary>
public class stream_compacting_safety: OneOffConfigurationsContext
{
    private static readonly object[] TheDeletedStream = [new Opened(), new AEvent(), new AEvent(), new Closed()];

    [Fact]
    public async Task a_guid_stream_folding_to_null_keeps_every_event()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, TheDeletedStream);
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Select(x => x.Data.GetType())
            .ShouldBe([typeof(Opened), typeof(AEvent), typeof(AEvent), typeof(Closed)]);

        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull().CompactedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task a_string_stream_folding_to_null_keeps_every_event()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<ClosableAggregate>(streamKey, TheDeletedStream);
        await theSession.SaveChangesAsync();

        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamKey);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamKey);
        events.Select(x => x.Data.GetType())
            .ShouldBe([typeof(Opened), typeof(AEvent), typeof(AEvent), typeof(Closed)]);
    }

    [Fact]
    public async Task a_cutoff_folding_to_null_keeps_the_stream_that_carried_on_past_it()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, TheDeletedStream);
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new Opened(), new AEvent());
        await theSession.SaveChangesAsync();

        // Closed at version 4, so this cutoff folds to nothing even though the stream is live today
        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId, x => x.Version = 4);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(6);
        events.ShouldAllBe(x => !x.Data.GetType().IsGenericType);

        var aggregate = await theSession.Events.AggregateStreamAsync<ClosableAggregate>(streamId);
        aggregate.ShouldNotBeNull().ACount.ShouldBe(1);
    }

    [Fact]
    public async Task the_archiver_is_not_told_about_a_compaction_that_will_not_happen()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, TheDeletedStream);
        await theSession.SaveChangesAsync();

        var archiver = new StubEventsArchiver();

        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId, x => x.Archiver = archiver);
        await theSession.SaveChangesAsync();

        archiver.LastEvents.ShouldBeNull();
    }

    [Fact]
    public async Task an_archiver_closed_over_the_session_runs_before_the_events_are_deleted()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, new Opened(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        var archiver = new SessionArchiver();

        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId, x => x.Archiver = archiver);
        await theSession.SaveChangesAsync();

        archiver.Calls.ShouldBe(1);
        archiver.Events.Select(x => x.Version).ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task an_archiver_closed_over_the_operations_type_still_runs()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, new Opened(), new AEvent());
        await theSession.SaveChangesAsync();

        var archiver = new StubEventsArchiver();

        await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId, x => x.Archiver = archiver);
        await theSession.SaveChangesAsync();

        archiver.LastEvents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task an_archiver_this_store_cannot_call_refuses_the_compaction()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<ClosableAggregate>(streamId, new Opened(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        var archiver = new UnusableArchiver();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Events.CompactStreamAsync<ClosableAggregate>(streamId, x => x.Archiver = archiver);
        });

        ex.Message.ShouldContain(nameof(UnusableArchiver));

        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchStreamAsync(streamId)).Count.ShouldBe(3);
    }

    private sealed class SessionArchiver: IEventsArchiver<IDocumentSession>
    {
        public int Calls { get; private set; }
        public IReadOnlyList<IEvent> Events { get; private set; } = [];

        public Task MaybeArchiveAsync<T>(IDocumentSession operations, StreamCompactingRequest<T> request,
            IReadOnlyList<IEvent> events, CancellationToken cancellation) where T : class
        {
            Calls++;
            Events = events;
            return Task.CompletedTask;
        }
    }

    // Closed over something Marten has no way to hand it. It is not an archiver Marten can run, and the
    // events are about to be deleted, so it has to be an error rather than a skipped step.
    private sealed class UnusableArchiver: IEventsArchiver<string>
    {
        public Task MaybeArchiveAsync<T>(string operations, StreamCompactingRequest<T> request,
            IReadOnlyList<IEvent> events, CancellationToken cancellation) where T : class
        {
            return Task.CompletedTask;
        }
    }
}

public record Opened;

public record Closed;

public class ClosableAggregate
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public int ACount { get; set; }

    public static ClosableAggregate Create(Opened _) => new();

    public void Apply(AEvent _) => ACount++;

    public bool ShouldDelete(Closed _) => true;
}
