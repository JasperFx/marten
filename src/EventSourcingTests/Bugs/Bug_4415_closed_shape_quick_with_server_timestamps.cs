using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// End-to-end smoke test for the closed-shape QuickWithServerTimestamps
/// path (#4415). The default v9 AppendMode is
/// <see cref="EventAppendMode.QuickWithServerTimestamps"/>, so this test
/// is the closest match to the "out of the box" experience with the
/// closed-shape flag flipped on.
/// </summary>
/// <remarks>
/// The QuickWithServerTimestamps mode sends one additional array parameter
/// to <c>mt_quick_append_events</c> — the per-event timestamps. The
/// dialect's function signature accepts it when this mode is on; the
/// closed-shape operation calls the base <c>writeTimestamps</c> helper.
/// </remarks>
public class Bug_4415_closed_shape_quick_with_server_timestamps : OneOffConfigurationsContext
{
    [Fact]
    public async Task quick_with_server_timestamps_round_trip_guid_identity()
    {
        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
            // AppendMode defaults to QuickWithServerTimestamps in v9, but
            // be explicit here to pin the test against the right path.
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        });

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Server-TS Quest" },
                new MembersJoined { Members = new[] { "Frodo" } });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new MembersJoined { Members = new[] { "Sam" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamId);
            state.ShouldNotBeNull();
            state.Version.ShouldBe(3);
        }

        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(3);
            events[0].Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("Server-TS Quest");
            // Timestamps must come back populated (server-side function
            // wrote them).
            foreach (var @event in events)
            {
                @event.Timestamp.ShouldNotBe(default);
            }
        }
    }

    [Fact]
    public async Task quick_with_server_timestamps_default_v9_config_works()
    {
        // v9 default is QuickWithServerTimestamps; flip closed-shape on and
        // make no other config tweaks. This is the headline "drop-in" path.
        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new QuestStarted { Name = "Default v9" });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var @event = (await query.Events.FetchStreamAsync(streamId)).Single();
            @event.Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("Default v9");
            @event.Timestamp.ShouldNotBe(default);
        }
    }
}
