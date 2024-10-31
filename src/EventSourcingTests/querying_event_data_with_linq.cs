using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using Marten;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Testing.Harness;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class querying_event_data_with_linq: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;
    private readonly MembersJoined joined1 = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
    private readonly MembersDeparted departed1 = new MembersDeparted { Members = new[] { "Thom" } };

    private readonly MembersJoined joined2 = new MembersJoined { Members = new string[] { "Nynaeve", "Egwene" } };
    private readonly MembersDeparted departed2 = new MembersDeparted { Members = new[] { "Matt" } };

    #region sample_query-against-event-data
    [Fact]
    public async Task can_query_against_event_type()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    #endregion

    [Fact]
    public async Task can_query_against_event_type_with_camel_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public async Task can_query_against_event_metadata()
    {
        var sql = theSession.Events
            .QueryAllRawEvents()
            .Where(x => x.Sequence >= 123)
            .Where(x => x.EventTypeName == "SomethingHappenedEvent")
            .Where(x => x.DotNetTypeName == "AlsoWrong")
            .OrderBy(x => x.EventTypeName)
            .Take(3)
            .ToCommand().CommandText;

        sql.ShouldNotContain("d.data ->> 'EventTypeName' = :p1");
        sql.ShouldNotContain("d.data ->> 'DotNetTypeName' = :p2");
    }

    [Fact]
    public async Task can_query_against_event_type_with_snake_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>().Where(x => x.Members.Contains("Matt"))
            .Single().Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public void will_not_blow_up_if_searching_for_events_before_event_store_is_warmed_up()
    {
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Any().ShouldBeFalse();
    }


    [Fact]
    public async Task can_query_against_event_type_with_different_schema_name_with_camel_casing()
    {
        StoreOptions(_ =>
        {
            _.Events.DatabaseSchemaName = SchemaName + "_events";

            _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

            _.Events.AddEventType(typeof(MembersDeparted));
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();


        theStore.StorageFeatures.FindMapping(typeof(MembersDeparted))
            .TableName.Schema.ShouldBe("querying_event_data_with_linq_events");

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public async Task can_fetch_all_events()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        var results = theSession.Events.QueryAllRawEvents().ToList();

        results.Count.ShouldBe(4);
    }

    #region sample_example_of_querying_for_event_data
    public void example_of_querying_for_event_data(IDocumentSession session, Guid stream)
    {
        var events = session.Events.QueryAllRawEvents()
            .Where(x => x.StreamId == stream)
            .OrderBy(x => x.Sequence)
            .ToList();
    }

    #endregion

    [Fact]
    public async Task can_fetch_all_events_after_now()
    {
        var now = DateTimeOffset.UtcNow;

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        var past = now.AddSeconds(-1);

        var results = theSession.Events.QueryAllRawEvents().Where(x => x.Timestamp > past).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public async Task can_fetch_all_events_before_now()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        var dbNow = (DateTime)theSession.Connection.CreateCommand().Sql("select now();").ExecuteScalar();
        var now = new DateTimeOffset(dbNow).AddSeconds(5);

        var all = theSession.Events.QueryAllRawEvents().ToList();

        var results = theSession.Events.QueryAllRawEvents()
            .Where(x => x.Timestamp < now).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public async Task can_fetch_events_by_sequence()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.Sequence <= 2).ShouldBe(2);
    }

    [Fact]
    public async Task can_fetch_by_version()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        await theSession.SaveChangesAsync();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.Version == 1).ShouldBe(2);
    }

    [Fact]
    public async Task can_search_by_stream()
    {
        var stream1 = theSession.Events.StartStream<Quest>(joined1, departed1).Id;
        var stream2 = theSession.Events.StartStream<Quest>(joined2, departed2).Id;

        await theSession.SaveChangesAsync();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.StreamId == stream1).ShouldBe(2);
    }

    [Fact]
    public async Task can_search_by_event_types()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent(), new CEvent(), new DEvent());
        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent(), new DEvent(), new DEvent());
        theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new CEvent(), new CEvent(), new DEvent());

        await theSession.SaveChangesAsync();

        #region sample_using_event_types_are

        var raw = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.EventTypesAre(typeof(CEvent), typeof(DEvent)))
            .ToListAsync();

        #endregion

        foreach (var e in raw)
        {
            ((e.Data is CEvent) || (e.Data is DEvent)).ShouldBeTrue();
        }
    }

    [Fact] // was GH-2777
    public async Task querying_with_linq_against_events_and_event_has_id_property()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Logger(new TestOutputMartenLogger(_output));
        }, true);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append("a", new DummyEvent("a"));

            await session.SaveChangesAsync();
        }

        await using (var session = theStore.QuerySession())
        {
            var ids = await session.Events.QueryRawEventDataOnly<DummyEvent>()
                .Select(d => d.Id)          // This causes the operation to fail
                .ToListAsync();
        }
    }

    [Fact] // was GH-2848
    public async Task can_make_the_query_with_distinct_and_count()
    {
        await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived())
            .Select(x => x.StreamKey)
            .Distinct()
            .CountAsync();
    }

    [Fact]
    public void generate_proper_sql_with_querying_against_event_id()
    {
        var eventsIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var command = theSession.Events.QueryAllRawEvents()
            .Where(e => e.Id.In(eventsIds))
            .Where(e => e.AnyTenant())
            .ToCommand();

        command.CommandText.ShouldContain("d.id = ANY(:p0)");
        command.CommandText.ShouldNotContain("CAST(d.data ->> 'Id' as uuid)");
    }

    /*
     * MORE!!!
     * Async everything
     */
    public querying_event_data_with_linq(ITestOutputHelper output)
    {
        _output = output;
        theStore.Advanced.Clean.DeleteAllEventDataAsync().GetAwaiter().GetResult();
    }
}

public record DummyEvent(string Id);
