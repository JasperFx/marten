using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Shouldly;
using Xunit;

namespace JasperFx.Events.ComplianceTests;

#region sample_compliance_evolve_aggregates

public record EvolveAEvent;

public record EvolveBEvent;

public record EvolveCEvent;

/// <summary>
/// Mutable aggregate using void Evolve(IEvent e) -- switches on the IEvent envelope
/// </summary>
public partial class MutableIEventEvolveAggregate
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }

    public void Evolve(IEvent e)
    {
        switch (e)
        {
            case IEvent<EvolveAEvent>:
                ACount++;
                break;
            case IEvent<EvolveBEvent>:
                BCount++;
                break;
            case IEvent<EvolveCEvent>:
                CCount++;
                break;
        }
    }
}

/// <summary>
/// Mutable aggregate using void Evolve(object o) -- switches on the event data
/// </summary>
public partial class MutableObjectEvolveAggregate
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }

    public void Evolve(object o)
    {
        switch (o)
        {
            case EvolveAEvent:
                ACount++;
                break;
            case EvolveBEvent:
                BCount++;
                break;
            case EvolveCEvent:
                CCount++;
                break;
        }
    }
}

/// <summary>
/// Immutable aggregate using TDoc Evolve(IEvent e) -- returns a new instance
/// </summary>
public partial record ImmutableIEventEvolveAggregate(Guid Id, int ACount = 0, int BCount = 0, int CCount = 0)
{
    public ImmutableIEventEvolveAggregate(): this(Guid.Empty) { }

    public ImmutableIEventEvolveAggregate Evolve(IEvent e)
    {
        return e switch
        {
            IEvent<EvolveAEvent> => this with { ACount = ACount + 1 },
            IEvent<EvolveBEvent> => this with { BCount = BCount + 1 },
            IEvent<EvolveCEvent> => this with { CCount = CCount + 1 },
            _ => this
        };
    }
}

/// <summary>
/// Immutable aggregate using TDoc Evolve(object o) -- returns a new instance
/// </summary>
public partial record ImmutableObjectEvolveAggregate(Guid Id, int ACount = 0, int BCount = 0, int CCount = 0)
{
    public ImmutableObjectEvolveAggregate(): this(Guid.Empty) { }

    public ImmutableObjectEvolveAggregate Evolve(object o)
    {
        return o switch
        {
            EvolveAEvent => this with { ACount = ACount + 1 },
            EvolveBEvent => this with { BCount = BCount + 1 },
            EvolveCEvent => this with { CCount = CCount + 1 },
            _ => this
        };
    }
}

/// <summary>
/// Mutable aggregate using async Task EvolveAsync(IEvent e, IQuerySession session).
/// The session parameter is written against the consumer-supplied
/// <c>ComplianceQuerySession</c> global alias so this one shared source file binds to
/// Marten's IQuerySession in Marten and Polecat's in Polecat.
/// </summary>
public partial class AsyncEvolveAggregate
{
    public Guid Id { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }

    public Task EvolveAsync(IEvent e, ComplianceQuerySession session)
    {
        switch (e)
        {
            case IEvent<EvolveAEvent>:
                ACount++;
                break;
            case IEvent<EvolveBEvent>:
                BCount++;
                break;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Immutable aggregate using async ValueTask&lt;TDoc&gt; EvolveAsync(IEvent e, IQuerySession session)
/// </summary>
public partial record ImmutableAsyncEvolveAggregate(Guid Id, int ACount = 0, int BCount = 0)
{
    public ImmutableAsyncEvolveAggregate(): this(Guid.Empty) { }

    public ValueTask<ImmutableAsyncEvolveAggregate> EvolveAsync(IEvent e, ComplianceQuerySession session)
    {
        var result = e switch
        {
            IEvent<EvolveAEvent> => this with { ACount = ACount + 1 },
            IEvent<EvolveBEvent> => this with { BCount = BCount + 1 },
            _ => this
        };

        return new ValueTask<ImmutableAsyncEvolveAggregate>(result);
    }
}

#endregion

/// <summary>
/// Behavior of self-aggregating types that use Evolve/EvolveAsync conventions instead of
/// Apply/Create. The store's source generator emits an IGeneratedSyncEvolver /
/// IGeneratedAsyncEvolver that delegates to the user's method.
/// </summary>
/// <remarks>
/// Every inline case asserts the *persisted* document via LoadDocumentAsync rather than re-folding
/// the stream: re-folding would pass even if the inline projection never wrote anything.
/// </remarks>
public abstract class SelfAggregatingEvolveCompliance<TFixture, TOperations, TQuerySession>
    : EventStoreComplianceSuite<TFixture, TOperations, TQuerySession>
    where TFixture : EventStoreComplianceFixture<TOperations, TQuerySession>, new()
    where TOperations : TQuerySession, IStorageOperations
{
    private static readonly Action<ComplianceStoreConfig> _configuration = config =>
    {
        config.SchemaName = "compliance_evolve";

        config.Snapshot<MutableIEventEvolveAggregate>(SnapshotLifecycle.Inline);
        config.Snapshot<MutableObjectEvolveAggregate>(SnapshotLifecycle.Inline);
        config.Snapshot<ImmutableIEventEvolveAggregate>(SnapshotLifecycle.Inline);
        config.Snapshot<ImmutableObjectEvolveAggregate>(SnapshotLifecycle.Inline);
        config.Snapshot<AsyncEvolveAggregate>(SnapshotLifecycle.Inline);
        config.Snapshot<ImmutableAsyncEvolveAggregate>(SnapshotLifecycle.Inline);
    };

    protected override Action<ComplianceStoreConfig> Configuration => _configuration;

    [Fact]
    public async Task mutable_ievent_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveBEvent(), new EvolveAEvent(),
            new EvolveCEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<MutableIEventEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task mutable_object_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveBEvent(), new EvolveCEvent(),
            new EvolveCEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<MutableObjectEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task immutable_ievent_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveAEvent(), new EvolveBEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<ImmutableIEventEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(0);
    }

    [Fact]
    public async Task immutable_object_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveBEvent(), new EvolveCEvent(), new EvolveAEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<ImmutableObjectEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task async_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveAEvent(), new EvolveBEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<AsyncEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
    }

    [Fact]
    public async Task immutable_async_evolve_inline()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveBEvent(), new EvolveAEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<ImmutableAsyncEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
    }

    [Fact]
    public async Task mutable_ievent_evolve_with_append_to_existing_stream()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveBEvent());
        await SaveChangesAsync(session);

        EventsFor(session).Append(streamId, new EvolveAEvent(), new EvolveCEvent());
        await SaveChangesAsync(session);

        var aggregate = await LoadDocumentAsync<MutableIEventEvolveAggregate>(session, streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task live_aggregation_with_evolve()
    {
        await using var session = OpenSession();

        var streamId = Guid.NewGuid();
        EventsFor(session).StartStream(streamId, new EvolveAEvent(), new EvolveBEvent(), new EvolveCEvent());
        await SaveChangesAsync(session);

        var aggregate =
            await EventsFor(session).AggregateStreamAsync<MutableIEventEvolveAggregate>(streamId, token: Cancellation);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }
}
