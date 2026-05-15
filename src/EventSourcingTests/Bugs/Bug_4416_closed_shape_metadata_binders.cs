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
            opts.EventGraph.UseClosedShapeStorage = true;
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
    public async Task headers_write_path_round_trips_via_codegen_read()
    {
        // Write via the closed-shape HeadersColumnBinder (session.Serializer.ToJson
        // → jsonb parameter); read via Marten's standard FetchStreamAsync. The
        // read path here still goes through the codegen-emitted
        // ApplyReaderDataToEvent because the closed-shape HeadersColumn
        // read-back isn't wired yet (needs ISerializer threading on
        // IEventTableColumn — #4416 part 2). So we toggle UseClosedShapeStorage
        // OFF for the read step to keep this test focused on the write path.
        //
        // Once #4416 part 2 lands, the read in this test will use the
        // closed-shape adapter and we can drop the two-store dance.

        StoreOptions(opts =>
        {
            opts.EventGraph.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.SetHeader("origin", "closed-shape-write");
            session.SetHeader("priority", "high");

            session.Events.StartStream(streamId,
                new QuestStarted { Name = "Headers Quest" });
            await session.SaveChangesAsync();
        }

        // Spin up a second store pointed at the same schema with the
        // closed-shape flag OFF so the read path uses codegen.
        await using var readerStore = SeparateStore(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        await using (var query = readerStore.QuerySession())
        {
            var events = (await query.Events.FetchStreamAsync(streamId)).ToArray();
            events.Length.ShouldBe(1);
            events[0].Headers.ShouldNotBeNull();
            events[0].Headers["origin"].ToString().ShouldBe("closed-shape-write");
            events[0].Headers["priority"].ToString().ShouldBe("high");
        }
    }
}
