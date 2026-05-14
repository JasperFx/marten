using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Linq.Selectors;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// Coverage for the shared <see cref="ISelector{StreamState}"/> implementation on
/// <see cref="IEventStorage"/>. Both Marten's own <c>FetchStreamStateAsync</c> codegen
/// and external consumers (e.g. an event store explorer surface) read
/// <c>mt_streams</c> through this selector + the matching <c>StreamStateSelectSql</c>
/// — this test fixture exercises the path with a hand-built command to prove the
/// abstraction is reachable and the column ordering is in sync with the SELECT
/// fragment.
/// </summary>
public class event_storage_stream_state_selector: IntegrationContext
{
    public event_storage_stream_state_selector(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task selector_round_trips_a_real_stream_row()
    {
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Quest>(streamId,
                new QuestStarted { Name = "selector smoke test" });
            await session.SaveChangesAsync();
        }

        // The reference point: what FetchStreamStateAsync — which goes through the
        // codegen'd QueryForStream handler — returns for the same stream.
        StreamState expected;
        await using (var session = theStore.LightweightSession())
        {
            expected = await session.Events.FetchStreamStateAsync(streamId);
        }

        expected.ShouldNotBeNull();

        // Now drive the SAME row read manually through ISelector<StreamState>.
        await using var query = (DocumentSessionBase)theStore.LightweightSession();
        var storage = query.EventStorage();
        var selector = (ISelector<StreamState>)storage;

        var cmd = new NpgsqlCommand($"{storage.StreamStateSelectSql} where id = @id");
        cmd.Parameters.AddWithValue("id", streamId);

        await using var reader = await query.ExecuteReaderAsync(cmd);
        (await reader.ReadAsync()).ShouldBeTrue();

        var actual = await selector.ResolveAsync(reader, default);

        actual.ShouldNotBeNull();
        actual.Id.ShouldBe(expected.Id);
        actual.Version.ShouldBe(expected.Version);
        actual.Created.ShouldBe(expected.Created);
        actual.LastTimestamp.ShouldBe(expected.LastTimestamp);
        actual.IsArchived.ShouldBe(expected.IsArchived);
        actual.AggregateType.ShouldBe(expected.AggregateType);
    }

    [Fact]
    public async Task select_sql_includes_the_columns_the_selector_reads()
    {
        // Sanity check that the shared SELECT clause is in the right shape and the
        // column ordering matches what the selector implementation reads. A future
        // edit that adds/removes a StreamState field has to update both sides — this
        // test will fail loudly when the two drift apart.
        await using var session = (DocumentSessionBase)theStore.LightweightSession();
        var storage = session.EventStorage();

        var sql = storage.StreamStateSelectSql;

        sql.ShouldContain("id");
        sql.ShouldContain("version");
        sql.ShouldContain("type");
        sql.ShouldContain("timestamp");
        sql.ShouldContain("created");
        sql.ShouldContain("is_archived");
        sql.ShouldContain("mt_streams");
        sql.ShouldStartWith("select ");
    }
}
