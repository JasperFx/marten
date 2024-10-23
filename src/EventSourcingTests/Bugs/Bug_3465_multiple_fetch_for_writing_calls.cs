using System;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3465_multiple_fetch_for_writing_calls : BugIntegrationContext
{
    [Fact]
    public async Task work_correctly()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<SomeProjection>();
            opts.Projections.LiveStreamAggregation<SomeOtherProjection>();
        });

        var streamId = CombGuidIdGeneration.NewGuid();
        theSession.Events.StartStream(streamId, new EventA(), new EventA(), new EventA());
        await theSession.SaveChangesAsync();
// stream version is now 3

        var otherSession = theStore.LightweightSession();
        var firstProjection = await otherSession.Events.FetchForWriting<SomeProjection>(streamId);

        firstProjection.StartingVersion.ShouldBe(3);
        firstProjection.Aggregate.ShouldNotBeNull();
        firstProjection.Aggregate.Version.ShouldBe(3);

// in another session, append more events

        theSession.Events.Append(streamId, new EventA(), new EventA());
        await theSession.SaveChangesAsync();
// stream version is now 5

        var secondProjection = await otherSession.Events.FetchForWriting<SomeOtherProjection>(streamId);
        secondProjection.Aggregate.ShouldNotBeNull();
        secondProjection.Aggregate.Version.ShouldBe(5);

// attempt to append
        otherSession.Events.Append(streamId, new EventA());

// should fail with concurrency error
// because current version of the stream (5) is ahead of the first optimistic lock (3)
        await otherSession.SaveChangesAsync();
    }

    [Fact]
    public async Task work_correctly_with_identity_map()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<SomeProjection>();
            opts.Projections.LiveStreamAggregation<SomeOtherProjection>();
        });

        using var session = theStore.IdentitySession();

        var streamId = CombGuidIdGeneration.NewGuid();
        session.Events.StartStream(streamId, new EventA(), new EventA(), new EventA());
        await session.SaveChangesAsync();
// stream version is now 3

        var otherSession = theStore.LightweightSession();
        var firstProjection = await otherSession.Events.FetchForWriting<SomeProjection>(streamId);

        firstProjection.StartingVersion.ShouldBe(3);
        firstProjection.Aggregate.ShouldNotBeNull();
        firstProjection.Aggregate.Version.ShouldBe(3);

// in another session, append more events

        session.Events.Append(streamId, new EventA(), new EventA());
        await session.SaveChangesAsync();
// stream version is now 5

        var secondProjection = await otherSession.Events.FetchForWriting<SomeOtherProjection>(streamId);
        secondProjection.Aggregate.ShouldNotBeNull();
        secondProjection.Aggregate.Version.ShouldBe(5);

// attempt to append
        otherSession.Events.Append(streamId, new EventA());

// should fail with concurrency error
// because current version of the stream (5) is ahead of the first optimistic lock (3)
        await otherSession.SaveChangesAsync();
    }
}

public class SomeProjection : IRevisioned
{
    public Guid Id { get; set; }
    public int A { get; set; }
    public void Apply(EventA e) => A++;
    public int Version { get; set; }
}

public class SomeOtherProjection : IRevisioned
{
    public Guid Id { get; set; }
    public int A { get; set; }
    public void Apply(EventA e) => A++;
    public int Version { get; set; }
}
