using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.Projections;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using IRevisioned = Marten.Metadata.IRevisioned;

namespace EventSourcingTests.FetchForWriting;

public class fetching_live_aggregates_for_writing: IntegrationContext
{
    public fetching_live_aggregates_for_writing(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override Task fixtureSetup()
    {
        return theStore.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    [Fact]
    public async Task fetch_new_stream_for_writing_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        var document = stream.Aggregate;

        document.Id.ShouldBe(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_new_stream_for_writing_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamId = Guid.NewGuid().ToString();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        var document = stream.Aggregate;

        document.Id.ShouldBe(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_exclusively_happy_path_for_writing_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        var document = stream.Aggregate;

        document.Id.ShouldBe(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier_sad_path()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();


        await using var otherSession = theStore.LightweightSession();
        var otherStream = await otherSession.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        await Should.ThrowAsync<StreamLockedException>(async () =>
        {
            // Try to load it again, but it's locked
            var stream = await theSession.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        });
    }

    [Fact]
    public async Task fetch_existing_stream_exclusively_happy_path_for_writing_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        var document = stream.Aggregate;

        document.Id.ShouldBe(streamId);

        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_string_identifier_sad_path()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();


        await using var otherSession = theStore.LightweightSession();
        var otherStream = await otherSession.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);

        await Should.ThrowAsync<StreamLockedException>(async () =>
        {
            // Try to load it again, but it's locked
            var stream = await theSession.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
        });
    }


    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier_with_expected_version()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId, 6);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        stream.AppendOne(new EEvent());
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier_with_expected_version_immediate_sad_path()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId, 5);
        });
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier_with_expected_version_sad_path_on_save_changes()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // This should be fine
        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId, 6);
        stream.AppendOne(new EEvent());

        // Get in between and run other events in a different session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamId, new EEvent());
            await otherSession.SaveChangesAsync();
        }

        // The version is now off
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }


    [Fact]
    public async Task helpful_exception_when_id_type_is_mismatched_1()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamId = Guid.NewGuid();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId, 6);
        });

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with strings");
    }

    [Fact]
    public async Task helpful_exception_when_id_type_is_mismatched_2()
    {
        UseStreamIdentity(StreamIdentity.AsGuid);

        var streamId = Guid.NewGuid().ToString();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId, 6);
        });

        ex.Message.ShouldBe("This Marten event store is configured to identify streams with Guids");
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_string_identifier_with_expected_version()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId, 6);
        stream.Aggregate.ShouldNotBeNull();
        stream.CurrentVersion.ShouldBe(6);

        stream.AppendOne(new EEvent());
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_string_identifier_with_expected_version_immediate_sad_path()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId, 5);
        });
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_string_identifier_with_expected_version_sad_path_on_save_changes()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // This should be fine
        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamId, 6);
        stream.AppendOne(new EEvent());

        // Get in between and run other events in a different session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamId, new EEvent());
            await otherSession.SaveChangesAsync();
        }

        // The version is now off
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task using_a_custom_projection_fetch_latest()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounter(), ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<CountedAggregate>(streamId, new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<CountedAggregate>(streamId);
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(3);
        aggregate.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task using_a_custom_projection_that_has_string_as_id_fetch_latest()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ExplicitCounterThatHasStringId(), ProjectionLifecycle.Async);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = $"simple|{Guid.NewGuid()}";
        theSession.Events.StartStream<CountedAsString>(streamId, new AEvent(), new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.FetchLatest<CountedAsString>(streamId);
        aggregate.ACount.ShouldBe(2);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(3);
        aggregate.Id.ShouldBe(streamId);
    }

    [Fact]
    public async Task warn_if_trying_to_fetch_multi_stream_projection()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new TotalsProjection(), ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await theSession.Events.FetchForWriting<Totals>(streamId);
        });
    }

    [Fact]
    public async Task work_correctly_with_multiple_calls()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<SomeProjection>();
            opts.Projections.LiveStreamAggregation<SomeOtherProjection>();
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

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
    public async Task work_correctly_for_multiple_calls_with_identity_map()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<SomeProjection>();
            opts.Projections.LiveStreamAggregation<SomeOtherProjection>();
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

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

public class SimpleAggregate : IRevisioned
{
    // This will be the aggregate version
    public int Version { get; set; }

    public Guid Id { get;
        set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }

    protected bool Equals(SimpleAggregate other)
    {
        return Version == other.Version && Id.Equals(other.Id) && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount && ECount == other.ECount;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SimpleAggregate)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Version, Id, ACount, BCount, CCount, DCount, ECount);
    }
}



public class SimpleAggregate2
{
    // This will be the aggregate version
    public int Version { get; set; }

    public Guid Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }
}

public class SimpleAggregateAsString
{
    // This will be the aggregate version
    public long Version { get; set; }


    public string Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(IEvent<AEvent> @event)
    {
        Id ??= @event.StreamKey;
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }

    protected bool Equals(SimpleAggregateAsString other)
    {
        return Version == other.Version && Id == other.Id && ACount == other.ACount && BCount == other.BCount && CCount == other.CCount && DCount == other.DCount && ECount == other.ECount;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SimpleAggregateAsString)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Version, Id, ACount, BCount, CCount, DCount, ECount);
    }
}

public class Totals
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public class TotalsProjection: MultiStreamProjection<Totals, Guid>
{
    public TotalsProjection()
    {
        CustomGrouping((_, events, group) =>
        {
            group.AddEvents(Guid.NewGuid(), events.Where(x => x.Data is AEvent));
            group.AddEvents(Guid.NewGuid(), events.Where(x => x.Data is BEvent));
            group.AddEvents(Guid.NewGuid(), events.Where(x => x.Data is CEvent));
            group.AddEvents(Guid.NewGuid(), events.Where(x => x.Data is DEvent));

            return Task.CompletedTask;
        });
    }

    public void Apply(AEvent e, Totals totals) => totals.Count++;
    public void Apply(BEvent e, Totals totals) => totals.Count++;
    public void Apply(CEvent e, Totals totals) => totals.Count++;
    public void Apply(DEvent e, Totals totals) => totals.Count++;
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
