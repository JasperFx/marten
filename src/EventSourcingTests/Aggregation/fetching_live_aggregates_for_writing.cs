using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class fetching_live_aggregates_for_writing: IntegrationContext
{
    public fetching_live_aggregates_for_writing(DefaultStoreFixture fixture): base(fixture)
    {
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

}

public class SimpleAggregate : IRevisioned
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
