using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class query_against_event_documents_Tests: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;
    private readonly MembersJoined joined1 = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
    private readonly MembersDeparted departed1 = new MembersDeparted { Members = new[] { "Thom" } };

    private readonly MembersJoined joined2 = new MembersJoined { Members = new string[] { "Nynaeve", "Egwene" } };
    private readonly MembersDeparted departed2 = new MembersDeparted { Members = new[] { "Matt" } };

    #region sample_query-against-event-data
    [Fact]
    public void can_query_against_event_type()
    {
        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        TheSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    #endregion

    [Fact]
    public void can_query_against_event_type_with_camel_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        TheSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public async Task can_query_against_event_metadata()
    {
        var sql = TheSession.Events
            .QueryAllRawEvents()
            .Where(x => x.Sequence >= 123)
            .Where(x => x.EventTypeName == "SomethingHappenedEvent")
            .Where(x => x.DotNetTypeName == "AlsoWrong")
            .OrderBy(x => x.EventTypeName)
            .Take(3)
            .ToCommand().CommandText;

        sql.ShouldNotContain("d.data ->> 'EventTypeName' = :p1", StringComparisonOption.Default);
        sql.ShouldNotContain("d.data ->> 'DotNetTypeName' = :p2", StringComparisonOption.Default);
    }

    [Fact]
    public void can_query_against_event_type_with_snake_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        TheSession.Events.QueryRawEventDataOnly<MembersDeparted>().Where(x => x.Members.Contains("Matt"))
            .Single().Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public void will_not_blow_up_if_searching_for_events_before_event_store_is_warmed_up()
    {
        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().Any().ShouldBeFalse();
    }


    [Fact]
    public void can_query_against_event_type_with_different_schema_name_with_camel_casing()
    {
        StoreOptions(_ =>
        {
            _.Events.DatabaseSchemaName = SchemaName + "_events";

            _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

            _.Events.AddEventType(typeof(MembersDeparted));
        });

        TheStore.Advanced.Clean.DeleteAllEventData();


        TheStore.StorageFeatures.FindMapping(typeof(MembersDeparted))
            .TableName.Schema.ShouldBe("query_against_event_documents_tests_events");

        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        TheSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        TheSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public void can_fetch_all_events()
    {
        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        var results = TheSession.Events.QueryAllRawEvents().ToList();

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
    public void can_fetch_all_events_after_now()
    {
        var now = DateTimeOffset.UtcNow;

        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        var past = now.AddSeconds(-1);

        var results = TheSession.Events.QueryAllRawEvents().Where(x => x.Timestamp > past).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public void can_fetch_all_events_before_now()
    {
        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        var dbNow = (DateTime)TheSession.Connection.CreateCommand().Sql("select now();").ExecuteScalar();
        var now = new DateTimeOffset(dbNow).AddSeconds(5);

        var all = TheSession.Events.QueryAllRawEvents().ToList();

        var results = TheSession.Events.QueryAllRawEvents()
            .Where(x => x.Timestamp < now).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public void can_fetch_events_by_sequence()
    {
        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryAllRawEvents()
            .Count(x => x.Sequence <= 2).ShouldBe(2);
    }

    [Fact]
    public void can_fetch_by_version()
    {
        TheSession.Events.StartStream<Quest>(joined1, departed1);
        TheSession.Events.StartStream<Quest>(joined2, departed2);

        TheSession.SaveChanges();

        TheSession.Events.QueryAllRawEvents()
            .Count(x => x.Version == 1).ShouldBe(2);
    }

    [Fact]
    public void can_search_by_stream()
    {
        var stream1 = TheSession.Events.StartStream<Quest>(joined1, departed1).Id;
        var stream2 = TheSession.Events.StartStream<Quest>(joined2, departed2).Id;

        TheSession.SaveChanges();

        TheSession.Events.QueryAllRawEvents()
            .Count(x => x.StreamId == stream1).ShouldBe(2);
    }

    /*
     * MORE!!!
     * Async everything
     */
    public query_against_event_documents_Tests(ITestOutputHelper output)
    {
        _output = output;
        TheStore.Advanced.Clean.DeleteAllEventData();
    }
}