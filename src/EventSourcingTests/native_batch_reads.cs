using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;
using IDocumentReadOperations = JasperFx.Events.Documents.IDocumentReadOperations;
using IEventStoreOperations = JasperFx.Events.IEventStoreOperations;

namespace EventSourcingTests;

/// <summary>
///     #5550 / jasperfx#930: the store-agnostic batch reads, implemented natively instead of riding
///     JasperFx's default.
/// </summary>
/// <remarks>
///     <para>
///     Both contracts ship default-implemented, and both defaults are CORRECT — they just make one
///     round trip per id. So the shared compliance suites pass either way, and every assertion about
///     results passes either way. What proves the override took effect is the round-trip COUNT, which
///     is what these tests measure.
///     </para>
///     <para>
///     The `LoadManyAsync` trap is the sharper one: Marten has sixteen overloads and none of them puts
///     the token last, so the contract's `(ids, token)` call matched none and bound to the default with
///     no compile error and no test failure. The compliance suite loads 2,500 documents through it.
///     </para>
/// </remarks>
public class native_batch_reads: OneOffConfigurationsContext
{
    private readonly RoundTripCounter _counter = new();

    public native_batch_reads()
    {
        StoreOptions(opts => opts.Projections.Snapshot<BatchReadAggregate>(JasperFx.Events.Projections.SnapshotLifecycle.Inline));
    }

    private sealed class RoundTripCounter: IMartenSessionLogger
    {
        public int RoundTrips;

        public void OnBeforeExecute(NpgsqlCommand command) => Interlocked.Increment(ref RoundTrips);
        public void OnBeforeExecute(NpgsqlBatch batch) => Interlocked.Increment(ref RoundTrips);

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }

    // ------------------------------------------------------------------ LoadManyAsync

    [Fact]
    public async Task load_many_by_guid_makes_one_round_trip()
    {
        var docs = Enumerable.Range(0, 25)
            .Select(i => new BatchReadDoc { Id = Guid.NewGuid(), Name = $"doc-{i}" })
            .ToArray();

        theSession.Store(docs);
        await theSession.SaveChangesAsync();

        await using var session = theStore.QuerySession();
        session.Logger = _counter;

        IDocumentReadOperations reads = session;
        var loaded = await reads.LoadManyAsync<BatchReadDoc>(docs.Select(x => x.Id), CancellationToken.None);

        loaded.Count.ShouldBe(docs.Length);

        // The JasperFx default would be 25. One is the whole point of the override.
        _counter.RoundTrips.ShouldBe(1);
    }

    [Fact]
    public async Task load_many_by_string_makes_one_round_trip()
    {
        StoreOptions(opts => opts.Schema.For<BatchReadStringDoc>().Identity(x => x.Id));

        var docs = Enumerable.Range(0, 25)
            .Select(i => new BatchReadStringDoc { Id = $"key-{i}", Name = $"doc-{i}" })
            .ToArray();

        theSession.Store(docs);
        await theSession.SaveChangesAsync();

        await using var session = theStore.QuerySession();
        session.Logger = _counter;

        IDocumentReadOperations reads = session;
        var loaded = await reads.LoadManyAsync<BatchReadStringDoc>(docs.Select(x => x.Id), CancellationToken.None);

        loaded.Count.ShouldBe(docs.Length);
        _counter.RoundTrips.ShouldBe(1);
    }

    [Fact]
    public async Task load_many_keeps_the_contract_semantics()
    {
        var a = new BatchReadDoc { Id = Guid.NewGuid(), Name = "a" };
        var b = new BatchReadDoc { Id = Guid.NewGuid(), Name = "b" };

        theSession.Store(a, b);
        await theSession.SaveChangesAsync();

        await using var session = theStore.QuerySession();
        IDocumentReadOperations reads = session;

        // A missing id is left out, and a repeated id yields its document once.
        var loaded = await reads.LoadManyAsync<BatchReadDoc>(
            new[] { b.Id, Guid.NewGuid(), a.Id, b.Id }, CancellationToken.None);

        loaded.Count.ShouldBe(2);
        loaded.Select(x => x.Id).OrderBy(x => x).ShouldBe(new[] { a.Id, b.Id }.OrderBy(x => x));
    }

    [Fact]
    public async Task load_many_with_no_ids_returns_empty_and_makes_no_round_trip()
    {
        await using var session = theStore.QuerySession();
        session.Logger = _counter;

        IDocumentReadOperations reads = session;

        // The string overload is asked about a string-identified document; asking it about a
        // Guid-identified one is a genuine id/type mismatch and throws, override or not.
        (await reads.LoadManyAsync<BatchReadDoc>(Array.Empty<Guid>(), CancellationToken.None)).ShouldBeEmpty();
        (await reads.LoadManyAsync<BatchReadStringDoc>(Array.Empty<string>(), CancellationToken.None))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task load_many_is_not_bounded_by_a_parameter_ceiling()
    {
        // Mirrors the compliance suite's 2,500-id fact, which is the one that was quietly making
        // 2,500 round trips.
        var docs = Enumerable.Range(0, 2_500)
            .Select(i => new BatchReadDoc { Id = Guid.NewGuid(), Name = $"W{i}" })
            .ToArray();

        await theStore.BulkInsertAsync(docs);

        await using var session = theStore.QuerySession();
        session.Logger = _counter;

        IDocumentReadOperations reads = session;
        var loaded = await reads.LoadManyAsync<BatchReadDoc>(docs.Select(x => x.Id), CancellationToken.None);

        loaded.Count.ShouldBe(docs.Length);
        _counter.RoundTrips.ShouldBe(1);
    }

    // ------------------------------------------------------------------ FetchManyForWriting

    [Fact]
    public async Task fetch_many_for_writing_makes_one_round_trip()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            theSession.Events.StartStream<BatchReadAggregate>(id, new BatchReadEvent());
        }

        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = _counter;

        IEventStoreOperations events = session.Events;
        var streams = await events.FetchManyForWriting<BatchReadAggregate>(ids);

        streams.Count.ShouldBe(ids.Length);

        // The JasperFx default would be at least one per id.
        _counter.RoundTrips.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_many_for_writing_returns_one_handle_per_id_in_order()
    {
        var existing = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var other = Guid.NewGuid();

        theSession.Events.StartStream<BatchReadAggregate>(existing, new BatchReadEvent());
        theSession.Events.StartStream<BatchReadAggregate>(other, new BatchReadEvent(), new BatchReadEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        var streams = await events.FetchManyForWriting<BatchReadAggregate>(new[] { missing, other, existing });

        streams.Count.ShouldBe(3);
        streams[0].Aggregate.ShouldBeNull();
        streams[1].Aggregate.ShouldNotBeNull();
        streams[1].Aggregate.Count.ShouldBe(2);
        streams[2].Aggregate.ShouldNotBeNull();
        streams[2].Aggregate.Count.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_many_for_writing_commits_appends_to_every_handle()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        theSession.Events.StartStream<BatchReadAggregate>(first, new BatchReadEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        // `second` does not exist yet -- a brand-new stream must be appendable through a handle too.
        var streams = await events.FetchManyForWriting<BatchReadAggregate>(new[] { first, second });
        streams[0].AppendOne(new BatchReadEvent());
        streams[1].AppendOne(new BatchReadEvent());

        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<BatchReadAggregate>(first))!.Count.ShouldBe(2);
        (await query.LoadAsync<BatchReadAggregate>(second))!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_many_for_writing_by_key_returns_one_handle_per_key_in_order()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<BatchReadStringAggregate>(JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        });

        var existing = "stream-one";
        var missing = "stream-missing";

        theSession.Events.StartStream<BatchReadStringAggregate>(existing, new BatchReadEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        var streams = await events.FetchManyForWriting<BatchReadStringAggregate>(new[] { missing, existing });

        streams.Count.ShouldBe(2);
        streams.Select(x => x.Key).ShouldBe(new[] { missing, existing });
        streams[0].Aggregate.ShouldBeNull();
        streams[1].Aggregate.ShouldNotBeNull();
    }

    [Fact]
    public async Task fetch_many_for_writing_guards_each_stream_on_its_own_version()
    {
        var contended = Guid.NewGuid();
        var untouched = Guid.NewGuid();

        theSession.Events.StartStream<BatchReadAggregate>(contended, new BatchReadEvent());
        theSession.Events.StartStream<BatchReadAggregate>(untouched, new BatchReadEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;
        var streams = await events.FetchManyForWriting<BatchReadAggregate>(new[] { contended, untouched });

        // A rival moves one of the two streams on after the handles were taken.
        await using (var rival = theStore.LightweightSession())
        {
            rival.Events.Append(contended, new BatchReadEvent());
            await rival.SaveChangesAsync();
        }

        streams[1].AppendOne(new BatchReadEvent());

        // The untouched stream is not guarded by the contended one's version, so this commits.
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<BatchReadAggregate>(untouched))!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_many_for_writing_works_for_an_async_projected_aggregate()
    {
        // The batch refuses to mix an EXCLUSIVE fetch with a non-exclusive one for an Async-projected
        // aggregate, because the non-exclusive handler brackets its reads in its own transaction and
        // the 'end' would release the exclusive row lock. FetchManyForWriting only ever enlists
        // non-exclusive fetches, so it is outside that rule -- but several self-transacting handlers
        // in one batch is the shape worth pinning, since the per-id default never produced it.
        StoreOptions(opts =>
            opts.Projections.Snapshot<BatchReadAsyncAggregate>(JasperFx.Events.Projections.SnapshotLifecycle.Async));

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        theSession.Events.StartStream<BatchReadAsyncAggregate>(first, new BatchReadEvent());
        theSession.Events.StartStream<BatchReadAsyncAggregate>(second, new BatchReadEvent(), new BatchReadEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        var streams = await events.FetchManyForWriting<BatchReadAsyncAggregate>(new[] { first, second });

        streams.Count.ShouldBe(2);
        streams[0].CurrentVersion.ShouldBe(1);
        streams[1].CurrentVersion.ShouldBe(2);

        streams[0].AppendOne(new BatchReadEvent());
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task fetch_many_for_writing_rejects_a_repeated_id()
    {
        var id = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        var ex = await Should.ThrowAsync<ArgumentException>(async () =>
            await events.FetchManyForWriting<BatchReadAggregate>(new[] { id, Guid.NewGuid(), id }));

        ex.Message.ShouldContain("more than once");
    }

    [Fact]
    public async Task fetch_many_for_writing_rejects_a_repeated_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<BatchReadStringAggregate>(JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        });

        await using var session = theStore.LightweightSession();
        IEventStoreOperations events = session.Events;

        await Should.ThrowAsync<ArgumentException>(async () =>
            await events.FetchManyForWriting<BatchReadStringAggregate>(new[] { "a", "b", "a" }));
    }

    [Fact]
    public async Task fetch_many_for_writing_with_no_ids_returns_no_handles_and_makes_no_round_trip()
    {
        await using var session = theStore.LightweightSession();
        session.Logger = _counter;

        IEventStoreOperations events = session.Events;

        (await events.FetchManyForWriting<BatchReadAggregate>(Array.Empty<Guid>())).ShouldBeEmpty();
        (await events.FetchManyForWriting<BatchReadAggregate>(Array.Empty<string>())).ShouldBeEmpty();

        _counter.RoundTrips.ShouldBe(0);
    }
}

public class BatchReadDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BatchReadStringDoc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public record BatchReadEvent;

public class BatchReadAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(BatchReadEvent _) => Count++;
}

public class BatchReadAsyncAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(BatchReadEvent _) => Count++;
}

public class BatchReadStringAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }

    public void Apply(BatchReadEvent _) => Count++;
}
