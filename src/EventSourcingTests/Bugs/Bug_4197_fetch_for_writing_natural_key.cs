using System;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_4197_fetch_for_writing_natural_key : OneOffConfigurationsContext
{
    // Types matching the user's repro
    public sealed record Bug4197AggregateKey(string Value);

    public sealed record Bug4197AggregateCreatedEvent(Guid Id, string Key);

    public sealed class Bug4197Aggregate
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public Bug4197AggregateKey Key { get; set; }

        [NaturalKeySource]
        public void Apply(Bug4197AggregateCreatedEvent e)
        {
            Id = e.Id;
            Key = new Bug4197AggregateKey(e.Key);
        }
    }

    [Fact]
    public async Task fetch_for_writing_with_natural_key_without_explicit_projection_registration()
    {
        // This matches the user's repro: no explicit projection registration,
        // just a self-aggregating type with [NaturalKey] and [NaturalKeySource].
        // Marten should auto-discover the aggregate and its natural key.
        StoreOptions(opts =>
        {
            // No explicit projection registration - relying on auto-discovery
        });

        // First call FetchForWriting to trigger auto-discovery, then apply schema
        await using var session1 = theStore.LightweightSession();
        var preCheck = await session1.Events.FetchForWriting<Bug4197Aggregate, Bug4197AggregateKey>(
            new Bug4197AggregateKey("nonexistent"));
        preCheck.Aggregate.ShouldBeNull();

        // Now the projection is auto-registered, apply the schema
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = theStore.LightweightSession();

        var aggregateId = Guid.NewGuid();
        var aggregateKey = new Bug4197AggregateKey("randomkeyvalue");
        var e = new Bug4197AggregateCreatedEvent(aggregateId, aggregateKey.Value);

        session.Events.StartStream<Bug4197Aggregate>(aggregateId, e);
        await session.SaveChangesAsync();

        // This should NOT throw: InvalidOperationException: Invalid identifier type for aggregate
        var stream = await session.Events.FetchForWriting<Bug4197Aggregate, Bug4197AggregateKey>(aggregateKey);

        stream.ShouldNotBeNull();
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Key.ShouldBe(aggregateKey);
    }

    [Fact]
    public async Task fetch_for_writing_with_natural_key_with_inline_snapshot()
    {
        // This is the working pattern from the existing tests
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Bug4197Aggregate>(SnapshotLifecycle.Inline);
        });

        await using var session = theStore.LightweightSession();

        var aggregateId = Guid.NewGuid();
        var aggregateKey = new Bug4197AggregateKey("randomkeyvalue");
        var e = new Bug4197AggregateCreatedEvent(aggregateId, aggregateKey.Value);

        session.Events.StartStream<Bug4197Aggregate>(aggregateId, e);
        await session.SaveChangesAsync();

        var stream = await session.Events.FetchForWriting<Bug4197Aggregate, Bug4197AggregateKey>(aggregateKey);

        stream.ShouldNotBeNull();
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.Key.ShouldBe(aggregateKey);
    }
}
