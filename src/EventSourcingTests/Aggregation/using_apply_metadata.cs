using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;



public class using_apply_metadata : OneOffConfigurationsContext
{
    #region sample_apply_metadata
    [Fact]
    public async Task apply_metadata()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ItemProjection>(ProjectionLifecycle.Inline);

            // THIS IS NECESSARY FOR THIS SAMPLE!
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        // Setting a header value on the session, which will get tagged on each
        // event captured by the current session
        theSession.SetHeader("last-modified-by", "Glenn Frey");

        var id = theSession.Events.StartStream<Item>(new ItemStarted("Blue item")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
        await theSession.SaveChangesAsync();

        var item = await theSession.LoadAsync<Item>(id);

        // RIP Glenn Frey, take it easy!
        item.LastModifiedBy.ShouldBe("Glenn Frey");
        item.Version.ShouldBe(4);
    }
#endregion

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task use_with_fetch_latest(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ItemProjection(), lifecycle);

            // THIS IS NECESSARY FOR THIS SAMPLE!
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        // Setting a header value on the session, which will get tagged on each
        // event captured by the current session
        theSession.SetHeader("last-modified-by", "Glenn Frey");

        var id = theSession.Events.StartStream<Item>(new ItemStarted("Blue item")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
        await theSession.SaveChangesAsync();

        var item = await theSession.Events.FetchLatest<Item>(id);

        // RIP Glenn Frey, take it easy!
        item.LastModifiedBy.ShouldBe("Glenn Frey");
        item.Version.ShouldBe(4);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task use_with_fetch_for_writing(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ItemProjection(), lifecycle);

            // THIS IS NECESSARY FOR THIS SAMPLE!
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        // Setting a header value on the session, which will get tagged on each
        // event captured by the current session
        theSession.SetHeader("last-modified-by", "Glenn Frey");

        var id = theSession.Events.StartStream<Item>(new ItemStarted("Blue item")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
        await theSession.SaveChangesAsync();

        var item = await theSession.Events.FetchForWriting<Item>(id);

        // RIP Glenn Frey, take it easy!
        item.Aggregate.LastModifiedBy.ShouldBe("Glenn Frey");
        item.Aggregate.Version.ShouldBe(4);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Live)]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task use_with_fetch_for_writing_for_specific_version(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new ItemProjection(), lifecycle);

            // THIS IS NECESSARY FOR THIS SAMPLE!
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        // Setting a header value on the session, which will get tagged on each
        // event captured by the current session
        theSession.SetHeader("last-modified-by", "Glenn Frey");

        var id = theSession.Events.StartStream<Item>(new ItemStarted("Blue item")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
        await theSession.SaveChangesAsync();

        var item = await theSession.Events.FetchForWriting<Item>(id, 4);

        // RIP Glenn Frey, take it easy!
        item.Aggregate.LastModifiedBy.ShouldBe("Glenn Frey");
        item.Aggregate.Version.ShouldBe(4);
    }

    [Fact]
    public async Task apply_metadata_on_record()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ItemRecordProjection>(ProjectionLifecycle.Inline);

            // THIS IS NECESSARY FOR THIS SAMPLE!
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        // Setting a header value on the session, which will get tagged on each
        // event captured by the current session
        theSession.SetHeader("last-modified-by", "Glenn Frey");

        var id = theSession.Events.StartStream<ItemRecord>(new ItemStarted("Blue item")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
        await theSession.SaveChangesAsync();

        var item = await theSession.LoadAsync<ItemRecord>(id);

        // RIP Glenn Frey, take it easy!
        item.LastModifiedBy.ShouldBe("Glenn Frey");
        item.Version.ShouldBe(4);

        var itemAggregation = await theSession.Events.AggregateStreamAsync<ItemRecord>(id);

        itemAggregation.LastModifiedBy.ShouldBe("Glenn Frey");
        itemAggregation.Version.ShouldBe(4);
    }
}




#region sample_using_ApplyMetadata

public class Item
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public bool Started { get; set; }
    public DateTimeOffset WorkedOn { get; set; }
    public bool Completed { get; set; }
    public string LastModifiedBy { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    public int Version { get; set; }
}

public record ItemStarted(string Description);

public record ItemWorked;

public record ItemFinished;

public class ItemProjection: SingleStreamProjection<Item>
{
    public void Apply(Item item, ItemStarted started)
    {
        item.Started = true;
        item.Description = started.Description;
    }

    public void Apply(Item item, IEvent<ItemWorked> worked)
    {
        // Nothing, I know, this is weird
    }

    public void Apply(Item item, ItemFinished finished)
    {
        item.Completed = true;
    }

    public override Item ApplyMetadata(Item aggregate, IEvent lastEvent)
    {
        // Apply the last timestamp
        aggregate.LastModified = lastEvent.Timestamp;

        var person = lastEvent.GetHeader("last-modified-by");

        aggregate.LastModifiedBy = person?.ToString() ?? "System";

        return aggregate;
    }
}

#endregion


public record ItemRecord(
    Guid Id,
    string Description,
    bool Started,
    DateTimeOffset WorkedOn,
    bool Completed,
    string LastModifiedBy,
    DateTimeOffset? LastModified,
    int Version);


public class ItemRecordProjection: SingleStreamProjection<ItemRecord>
{
    public ItemRecord Create(ItemStarted started)
    {
        return new ItemRecord(
            Guid.Empty,
            started.Description,
            true,
            default,
            false,
            string.Empty,
            null,
            0);
    }

    public void Apply(ItemRecord item, IEvent<ItemWorked> worked)
    {
        // Nothing, I know, this is weird
    }

    public ItemRecord Apply(ItemRecord item, ItemFinished finished)
    {
        return item with { Completed = true };
    }

    public override ItemRecord ApplyMetadata(ItemRecord aggregate, IEvent lastEvent)
    {
        var person = lastEvent.GetHeader("last-modified-by");
        return aggregate with
        {
            LastModified = lastEvent.Timestamp,
            LastModifiedBy = person?.ToString() ?? "System"
        };
    }
}
