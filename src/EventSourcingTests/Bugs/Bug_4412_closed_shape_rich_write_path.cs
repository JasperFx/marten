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
/// End-to-end smoke test for the closed-shape Rich-mode write path
/// (#4412 + #4413): flips <c>UseClosedShapeStorage = true</c>, sets
/// <c>AppendMode = Rich</c> (the closed-shape adapter currently only wires
/// the Rich path's write operations — Quick / QuickWithServerTimestamps
/// land in #4414 / #4415), and exercises:
/// </summary>
/// <list type="number">
///   <item><see cref="ClosedShapeEventDocumentStorage.AppendEvent"/> via
///         <c>RichEventStorage.AppendEvent</c></item>
///   <item><see cref="ClosedShapeEventDocumentStorage.InsertStream"/> via
///         <c>RichEventStorage.InsertStream</c></item>
///   <item><see cref="ClosedShapeEventDocumentStorage.UpdateStreamVersion"/>
///         via <c>RichEventStorage.UpdateStreamVersion</c></item>
///   <item><see cref="ClosedShapeEventDocumentStorage.QueryForStream"/> via
///         <c>RichEventStorage.QueryForStream</c></item>
///   <item>Read-side <c>ApplyReaderDataToEvent</c> (#4411).</item>
/// </list>
public class Bug_4412_closed_shape_rich_write_path : OneOffConfigurationsContext
{
    [Fact]
    public async Task start_stream_then_append_then_read_back_round_trip_guid_identity()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
        });

        var streamId = Guid.NewGuid();

        // 1) StartStream — exercises InsertStream + per-event AppendEvent.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Closed-shape write-path Quest" },
                new MembersJoined { Members = new[] { "Frodo", "Sam" } });
            await session.SaveChangesAsync();
        }

        // 2) Append to an existing stream — exercises QueryForStream (via
        //    RichStreamAppendingStep prefetch), UpdateStreamVersion, and
        //    AppendEvent.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new MembersDeparted { Members = new[] { "Sam" } });
            await session.SaveChangesAsync();
        }

        // 3) Read back via FetchStreamStateAsync — exercises QueryForStream
        //    directly through the IEventStorage contract.
        await using (var query = theStore.QuerySession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamId);
            state.ShouldNotBeNull();
            state.Id.ShouldBe(streamId);
            state.Version.ShouldBe(3);
            state.IsArchived.ShouldBeFalse();
        }

        // 4) Read back via FetchStreamAsync — exercises the standard event
        //    query path, which routes through the closed-shape adapter's
        //    ApplyReaderDataToEvent (#4411).
        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(3);
            events[0].Version.ShouldBe(1);
            events[1].Version.ShouldBe(2);
            events[2].Version.ShouldBe(3);

            events[0].StreamId.ShouldBe(streamId);
            events[1].StreamId.ShouldBe(streamId);
            events[2].StreamId.ShouldBe(streamId);

            events[0].Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("Closed-shape write-path Quest");
            events[1].Data.ShouldBeOfType<MembersJoined>().Members.ShouldBe(new[] { "Frodo", "Sam" });
            events[2].Data.ShouldBeOfType<MembersDeparted>().Members.ShouldBe(new[] { "Sam" });

            // Sequences should be monotonically increasing.
            events[0].Sequence.ShouldBeLessThan(events[1].Sequence);
            events[1].Sequence.ShouldBeLessThan(events[2].Sequence);
        }
    }

    [Fact]
    public async Task start_stream_then_append_then_read_back_round_trip_string_identity()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamKey = "quest-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey,
                new QuestStarted { Name = "String-identity Quest" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamKey,
                new MembersJoined { Members = new[] { "Aragorn" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamKey);
            state.ShouldNotBeNull();
            state.Key.ShouldBe(streamKey);
            state.Version.ShouldBe(2);
        }

        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamKey)).ToArray();
            events.Length.ShouldBe(2);
            events[0].StreamKey.ShouldBe(streamKey);
            events[1].StreamKey.ShouldBe(streamKey);
            events[0].Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("String-identity Quest");
            events[1].Data.ShouldBeOfType<MembersJoined>().Members.ShouldBe(new[] { "Aragorn" });
        }
    }
}
