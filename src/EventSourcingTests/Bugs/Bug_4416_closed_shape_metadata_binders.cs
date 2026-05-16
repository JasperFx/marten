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
/// Covers the metadata-column binders wired in #4416 — <c>CausationId</c>,
/// <c>CorrelationId</c>, <c>UserName</c>, and the write-side of
/// <c>Headers</c> (read-side Headers needs ISerializer threading on the
/// <c>IEventTableColumn</c> surface and lands as part 2 of #4416).
/// </summary>
/// <remarks>
/// The closed-shape adapter only accepts these metadata flags after each
/// binder is wired in <c>PostgresEventStoreDialect.SelectRichMetadataBinders</c>
/// — the default branch throws <see cref="NotSupportedException"/> for
/// any name the dialect doesn't recognize, so this test exercises the
/// happy path for every binder that's currently lit up.
/// </remarks>
public class Bug_4416_closed_shape_metadata_binders : OneOffConfigurationsContext
{
    [Fact]
    public async Task scalar_metadata_binders_round_trip_under_closed_shape_storage()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });

        const string causation = "cause-4416";
        const string correlation = "corr-4416";
        const string user = "tester-4416";

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.CausationId = causation;
            session.CorrelationId = correlation;
            session.LastModifiedBy = user;

            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Metadata-binders Quest" },
                new MembersJoined { Members = new[] { "Frodo" } });
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
            }
        }
    }

    [Fact]
    public async Task headers_round_trip_under_closed_shape_storage()
    {
        // Closed-shape end-to-end including Headers — both write
        // (HeadersColumnBinder) and read (HeadersColumn.ReadValueSync via
        // the serializer-aware IEventTableColumn overload added in
        // #4416 part 2).
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.SetHeader("origin", "closed-shape-round-trip");
            session.SetHeader("priority", "high");

            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Headers Quest" });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(1);
            events[0].Headers.ShouldNotBeNull();
            events[0].Headers["origin"].ToString().ShouldBe("closed-shape-round-trip");
            events[0].Headers["priority"].ToString().ShouldBe("high");
        }
    }

    [Fact]
    public async Task event_skipping_flag_does_not_break_rich_closed_shape_path()
    {
        // EnableEventSkippingInProjectionsOrSubscriptions adds an `is_skipped`
        // bool column to mt_events with DefaultValueByExpression("FALSE"). It's
        // a plain TableColumn (not IEventTableColumn), so it's filtered out of
        // EventsTable.SelectColumns() and never reaches SelectRichMetadataBinders.
        // This test pins that contract: enabling the flag with the closed-shape
        // adapter must NOT fail the descriptor build.
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new QuestStarted { Name = "Skipping-flag Quest" });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var @event = (await query.Events.FetchStreamAsync(streamId)).Single();
            @event.Data.ShouldBeOfType<QuestStarted>().Name.ShouldBe("Skipping-flag Quest");
        }
    }

    [Fact]
    public async Task all_metadata_binders_together_round_trip()
    {
        // The full matrix — every metadata flag that has a closed-shape
        // binder wired in #4416 part 1 + part 2.
        StoreOptions(opts =>
        {
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        const string causation = "cause-mix";
        const string correlation = "corr-mix";
        const string user = "tester-mix";

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.CausationId = causation;
            session.CorrelationId = correlation;
            session.LastModifiedBy = user;
            session.SetHeader("ix", "1");

            session.Events.StartStream(streamId, new QuestStarted { Name = "Full-mix Quest" });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var @event = (await query.Events.FetchStreamAsync(streamId)).Single();
            @event.CausationId.ShouldBe(causation);
            @event.CorrelationId.ShouldBe(correlation);
            @event.UserName.ShouldBe(user);
            @event.Headers.ShouldNotBeNull();
            @event.Headers["ix"].ToString().ShouldBe("1");
        }
    }
}
