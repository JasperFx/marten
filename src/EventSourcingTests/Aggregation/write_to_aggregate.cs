using System;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace EventSourcingTests.Aggregation;

public class write_to_aggregate : IntegrationContext
{
    public write_to_aggregate(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task write_to_Guid_aggregate_happy_path_sync()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregate>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_exclusive_to_Guid_aggregate_happy_path_sync()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteExclusivelyToAggregate<SimpleAggregate>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_Guid_aggregate_happy_path_async()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregate>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }


    [Fact]
    public async Task write_to_Guid_exclusively_aggregate_happy_path_async()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteExclusivelyToAggregate<SimpleAggregate>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_Guid_aggregate_happy_path_sync_with_expected_version()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregate>(streamId, 6,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_Guid_aggregate_happy_path_async_with_expected_version()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregate>(streamId, 6,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ECount.ShouldBe(1);
    }






    [Fact]
    public async Task write_to_string_aggregate_happy_path_sync()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregateAsString>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }


    [Fact]
    public async Task write_exclusively_to_string_aggregate_happy_path_sync()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteExclusivelyToAggregate<SimpleAggregateAsString>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_string_aggregate_happy_path_async()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregateAsString>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_exclusively_to_string_aggregate_happy_path_async()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteExclusivelyToAggregate<SimpleAggregateAsString>(streamId,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_string_aggregate_happy_path_sync_with_expected_version()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregateAsString>(streamId, 6,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }

    [Fact]
    public async Task write_to_string_aggregate_happy_path_async_with_expected_version()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new BEvent(), new BEvent(),
            new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.WriteToAggregate<SimpleAggregateAsString>(streamId, 6,stream =>
        {
            stream.Aggregate.ShouldNotBeNull();
            stream.CurrentVersion.ShouldBe(6);

            stream.AppendOne(new EEvent());

            return Task.CompletedTask;
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        document.ECount.ShouldBe(1);
    }


}
