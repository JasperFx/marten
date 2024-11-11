using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.FetchForWriting;

public class fetching_inline_aggregates_for_writing : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public fetching_inline_aggregates_for_writing(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task fetch_new_stream_for_writing_Guid_identifier()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));

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
    public async Task revision_is_updated_after_quick_appending_with_IRevisioned()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline);
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseIdentityMapForInlineAggregates = true;
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var document = await theSession.LoadAsync<SimpleAggregate>(streamId);
        document.Version.ShouldBe(6);
    }

    [Fact]
    public async Task revision_is_updated_after_quick_appending_with_custom_mapped_version()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate2>(ProjectionLifecycle.Inline).Metadata(m =>
            {
                m.Revision.MapTo(x => x.Version);
            });

            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseIdentityMapForInlineAggregates = true;
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate2>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        theSession.Logger = new TestOutputMartenLogger(_output);
        await theSession.SaveChangesAsync();

        var document = await theSession.LoadAsync<SimpleAggregate2>(streamId);
        document.Version.ShouldBe(6);
    }

    [Fact]
    public async Task fetch_new_stream_for_writing_Guid_identifier_exception_handling()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var sameStream = theSession.Events.StartStream(streamId, new AEvent());
        await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        var document = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        document.ACount.ShouldBe(1);
        document.BCount.ShouldBe(3);
        document.CCount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_existing_stream_for_writing_Guid_identifier()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));

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
    public async Task fetch_existing_stream_for_writing_Guid_identifier_multi_tenanted()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline).MultiTenanted();
        });

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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });


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
    public async Task fetch_existing_stream_for_writing_string_identifier_multi_tenanted()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline).MultiTenanted();
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });


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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));


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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));


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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));


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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));


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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline));


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
    public async Task fetch_existing_stream_for_writing_string_identifier_with_expected_version()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

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

    public static void using_identity_map_for_inline_aggregates()
    {
        #region sample_use_identity_map_for_inline_aggregates

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
            {
                opts.Connection("some connection string");

                // Force Marten to use the identity map for only the aggregate type
                // that is the targeted "T" in FetchForWriting<T>() when using
                // an Inline projection for the "T". Saves on Marten doing an extra
                // database fetch of the same data you already fetched from FetchForWriting()
                // when Marten needs to apply the Inline projection as part of SaveChanges()
                opts.Events.UseIdentityMapForInlineAggregates = true;
            })
            // This is non-trivial performance optimization if you never
            // need identity map mechanics in your commands or query handlers
            .UseLightweightSessions();

        #endregion
    }

    [Fact]
    public async Task silently_turns_on_identity_map_for_inline_aggregates()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(ProjectionLifecycle.Inline);
            opts.Events.UseIdentityMapForInlineAggregates = true;
        });

        var streamId = Guid.NewGuid();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.Aggregate.ShouldBeNull();
        stream.CurrentVersion.ShouldBe(0);

        stream.AppendOne(new AEvent());
        stream.AppendMany(new BEvent(), new BEvent(), new BEvent());
        stream.AppendMany(new CEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var session = theStore.LightweightSession();
        var existing = await session.Events.FetchForWriting<SimpleAggregate>(streamId);

        // Should already be using the identity map
        var loadAgain = await session.LoadAsync<SimpleAggregate>(streamId);
        loadAgain.ShouldBeSameAs(existing.Aggregate);

        // Append to the stream and see that the existing aggregate is changed
        existing.AppendOne(new AEvent());
        await session.SaveChangesAsync();

        // 1 from the original version, another we just appended
        existing.Aggregate.ACount.ShouldBe(2);

        using var query = theStore.QuerySession();
        var loadedFresh = await query.LoadAsync<SimpleAggregate>(streamId);
        loadedFresh.ACount.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_for_writing_cache()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<TestProjection2>(ProjectionLifecycle.Inline);
            opts.Schema
                .For<TestAggregate>()
                .Identity(x => x.StreamKey);
        });

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream(streamKey, new NamedEvent2("foo"));
        await theSession.SaveChangesAsync();

        var test = await theSession.Events.FetchForWriting<TestAggregate>(streamKey);
        test.Aggregate.Name.ShouldBe("foo");

        test.AppendOne(new NamedEvent2("bar"));
        //await theSession.Events.AppendOptimistic(streamKey, new NamedEvent2("bar")); If I commented the two lines above and uncommented this one it works fine
        await theSession.SaveChangesAsync();

        test = await theSession.Events.FetchForWriting<TestAggregate>(streamKey);
        test.Aggregate.Name.ShouldBe("bar");
    }

}

public record NamedEvent2(string Name);

public class TestProjection2: SingleStreamProjection<TestAggregate>
{
    public TestAggregate Create(NamedEvent2 @event)
        => new TestAggregate(@event.Name);

    public TestAggregate Apply(NamedEvent2 @event, TestAggregate aggregate)
        => aggregate with { Name = @event.Name };
}

public record TestAggregate
{
    public TestAggregate(string name)
    {
        Name = name;
    }

    [Identity]
    public string StreamKey { get; set; } = null!;

    public string Name { get; set; } = null!;
}

