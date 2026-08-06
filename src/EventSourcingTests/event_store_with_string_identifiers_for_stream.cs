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
        table.Columns[0].Name.ShouldBe("id");
    }

    [Fact]
    public void smoke_test_being_able_to_create_database_objects()
    {
        theStore.Tenancy.Default.Database.EnsureStorageExists(typeof(EventGraph));
    }

    // ported: the append / fetch-stream / fetch-state behaviour these four tests asserted
    // (try_to_insert_event_with_string_identifiers, _non_typed, fetch_state, fetch_state_async) now
    // lives in JasperFx.Events.ComplianceTests.StringStreamIdentityCompliance, which asserts more of
    // it -- event count, versions, payload type, and the unknown-key null. fetch_state and
    // fetch_state_async were also byte-identical to each other apart from `using` vs `await using`.
    //
    // Kept here deliberately: the two DDL tests above (varchar primary key on mt_streams, storage
    // creation), which are PostgreSQL storage layout and out of compliance scope; the
    // sample_eventstore-configure-stream-identity doc region in the constructor; and
    // store_on_multiple_streams_at_a_time below, which covers two streams in a single unit of work
    // and has no compliance equivalent.

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
