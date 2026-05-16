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
/// End-to-end smoke test for the closed-shape Quick-mode write path
/// (#4414). Same shape as <c>Bug_4412_closed_shape_rich_write_path</c>
/// but with <c>AppendMode = Quick</c>, exercising
/// <c>QuickEventStorage.{QuickAppendEvents, InsertStream,
/// UpdateStreamVersion, QueryForStream}</c> + the
/// <c>mt_quick_append_events</c> server function call.
/// </summary>
public class Bug_4414_closed_shape_quick_write_path : OneOffConfigurationsContext
{
    [Fact]
    public async Task quick_round_trip_guid_identity()
    {
        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Quick;
        });

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Quick Quest" },
                new MembersJoined { Members = new[] { "Frodo", "Sam" } });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new MembersDeparted { Members = new[] { "Sam" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamId);
            state.ShouldNotBeNull();
            state.Id.ShouldBe(streamId);
            state.Version.ShouldBe(3);
        }

        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(3);
            events[0].Version.ShouldBe(1);
            events[1].Version.ShouldBe(2);
            events[2].Version.ShouldBe(3);
            events[0].StreamId.ShouldBe(streamId);
            events[0].Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("Quick Quest");
            events[1].Data.ShouldBeOfType<MembersJoined>().Members.ShouldBe(new[] { "Frodo", "Sam" });
            events[2].Data.ShouldBeOfType<MembersDeparted>().Members.ShouldBe(new[] { "Sam" });
        }
    }

    [Fact]
    public async Task quick_round_trip_string_identity()
    {
        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamKey = "quest-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey, new QuestStarted { Name = "Quick String Quest" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamKey, new MembersJoined { Members = new[] { "Aragorn" } });
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
        }
    }

    [Fact]
    public async Task quick_round_trip_with_scalar_metadata()
    {
        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        const string causation = "cause-quick";
        const string correlation = "corr-quick";
        const string user = "tester-quick";

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.CausationId = causation;
            session.CorrelationId = correlation;
            session.LastModifiedBy = user;
            session.SetHeader("origin", "quick-write");

            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Quick Meta Quest" },
                new MembersJoined { Members = new[] { "Boromir" } });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(2);
            foreach (var @event in events)
            {
                @event.CausationId.ShouldBe(causation);
                @event.CorrelationId.ShouldBe(correlation);
                @event.UserName.ShouldBe(user);
                @event.Headers.ShouldNotBeNull();
                @event.Headers["origin"].ToString().ShouldBe("quick-write");
            }
        }
    }
}
