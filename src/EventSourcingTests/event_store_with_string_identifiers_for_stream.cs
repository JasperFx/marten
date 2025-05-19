using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class event_store_with_string_identifiers_for_stream: OneOffConfigurationsContext
{
    public event_store_with_string_identifiers_for_stream()
    {
        StoreOptions(storeOptions =>
        {
            #region sample_eventstore-configure-stream-identity
            storeOptions.Events.StreamIdentity = StreamIdentity.AsString;
            storeOptions.Projections.Snapshot<QuestPartyWithStringIdentifier>(SnapshotLifecycle.Async);
            #endregion
        });
    }

    [Fact]
    public void use_string_id_if_as_string_identifiers()
    {
        var events = new EventGraph(new StoreOptions()) { StreamIdentity = StreamIdentity.AsString };

        var table = new StreamsTable(events);

        var pk = table.Columns.Single(x => x.IsPrimaryKey);

        pk.Type.ShouldBe("varchar");
        pk.Name.ShouldBe("id");
        table.Columns.First().Name.ShouldBe("id");
    }

    [Fact]
    public void smoke_test_being_able_to_create_database_objects()
    {
        theStore.Tenancy.Default.Database.EnsureStorageExists(typeof(EventGraph));
    }

    [Fact]
    public async Task try_to_insert_event_with_string_identifiers()
    {
        using (var session = theStore.LightweightSession())
        {
            session.Events.Append("First", new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task try_to_insert_event_with_string_identifiers_non_typed()
    {
        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream("First", new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            (await session.Events.FetchStreamAsync("First")).Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task fetch_state()
    {
        using (var session = theStore.LightweightSession())
        {
            session.Events.Append("First", new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync("First");
            state.Key.ShouldBe("First");
            state.Version.ShouldBe(2);
        }
    }

    [Fact]
    public async Task fetch_state_async()
    {
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append("First", new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync("First");
            state.Key.ShouldBe("First");
            state.Version.ShouldBe(2);
        }
    }

    [Fact]
    public async Task store_on_multiple_streams_at_a_time()
    {
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append("First", new MembersJoined(), new MembersJoined());
            session.Events.Append("Second", new MembersJoined(), new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync("First");
            state.Key.ShouldBe("First");
            state.Version.ShouldBe(2);

            var state2 = await session.Events.FetchStreamStateAsync("Second");
            state2.Key.ShouldBe("Second");
            state2.Version.ShouldBe(3);
        }
    }

}
