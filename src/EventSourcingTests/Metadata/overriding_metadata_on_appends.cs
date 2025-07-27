using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Metadata;

public class overriding_metadata : OneOffConfigurationsContext
{
    public overriding_metadata()
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });
    }

    protected EventAppendMode EventAppendMode
    {
        set
        {
            StoreOptions(opts =>
            {
                opts.Events.AppendMode = value;
                opts.Events.MetadataConfig.CorrelationIdEnabled = true;
                opts.Events.MetadataConfig.CausationIdEnabled = true;
                opts.Events.MetadataConfig.HeadersEnabled = true;
                opts.Events.MetadataConfig.UserNameEnabled = true;
            });
        }
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.Quick)]
    public async Task capture_user_name_information(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        theSession.LastModifiedBy = "Larry Bird";

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var action = theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[0].UserName = "Kevin McHale";

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].UserName.ShouldBe("Kevin McHale");
        events[1].UserName.ShouldBe("Larry Bird");
        events[2].UserName.ShouldBe("Larry Bird");

        // Should write another test, but I'm doing it here!
        var celtics = await query.Events.QueryAllRawEvents().Where(x => x.UserName == "Larry Bird").ToListAsync();
        celtics.Count.ShouldBeGreaterThanOrEqualTo(2);
    }


    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.Quick)]
    public async Task override_timestamp_on_start(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var action = theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[0].Timestamp = timestamp;

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].Timestamp.ShouldBe(timestamp);
    }


    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.Quick)]
    public async Task override_timestamp_on_start_with_wrapper(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var e1 = new AEvent().AsEvent().AtTimestamp(timestamp);

        theSession.Events.StartStream(streamId, e1, new BEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].Timestamp.ShouldBe(timestamp);
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.QuickWithServerTimestamps)]
    public async Task override_timestamp_on_append(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var action = theSession.Events.Append(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[0].Timestamp = timestamp;

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].Timestamp.ShouldBe(timestamp);
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.QuickWithServerTimestamps)]
    public async Task override_timestamp_on_append_with_wrapper(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var e1 = Event.For(new AEvent());
        e1.Timestamp = timestamp;
        theSession.Events.Append(streamId, e1, new BEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].Timestamp.ShouldBe(timestamp);
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.Quick)]
    public async Task override_event_id_on_start(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        var eventId = Guid.NewGuid();

        // Just need a time that will be easy to assert on that is in the past
        var timestamp = (DateTimeOffset)DateTime.Today.Subtract(1.Hours()).ToUniversalTime();

        var action = theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[1].Id = eventId;

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[1].Id.ShouldBe(eventId);
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.Quick)]
    public async Task override_correlation_and_causation(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        theSession.CorrelationId = Guid.NewGuid().ToString();
        theSession.CausationId = Guid.NewGuid().ToString();

        var fakeCorrelation = Guid.NewGuid().ToString();
        var fakeCausation = Guid.NewGuid().ToString();

        var action = theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[0].CausationId = fakeCausation;
        action.Events[0].CorrelationId = fakeCorrelation;

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].CorrelationId.ShouldBe(fakeCorrelation);
        events[0].CausationId.ShouldBe(fakeCausation);

        events[1].CorrelationId.ShouldBe(theSession.CorrelationId);
        events[1].CausationId.ShouldBe(theSession.CausationId);
    }

    [Theory]
    [InlineData(JasperFx.Events.EventAppendMode.Rich)]
    [InlineData(JasperFx.Events.EventAppendMode.QuickWithServerTimestamps)]
    public async Task set_header_on_individual_events(EventAppendMode mode)
    {
        EventAppendMode = mode;
        var streamId = Guid.NewGuid();

        var action = theSession.Events.StartStream(streamId, new AEvent(), new BEvent(), new CEvent());
        action.Events[0].SetHeader("color", "red");
        action.Events[1].SetHeader("color", "blue");
        action.Events[2].SetHeader("color", "green");

        await theSession.SaveChangesAsync();

        using var query = theStore.QuerySession();

        var events = await query.Events.FetchStreamAsync(streamId);

        events[0].Headers["color"].ShouldBe("red");
        events[1].Headers["color"].ShouldBe("blue");
        events[2].Headers["color"].ShouldBe("green");
    }

    /* TODO
     Do this w/ FetchForWriting where you pass in Event<T> on new and old, quick and rich
     Append by passing in Event<T>
     StartStream by passing in Event<T>
        Watch the migration w/ the new timetz[]
     */
}
