using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Metadata;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// Regression for jasperfx/marten#4428.
///
/// When <c>StoreOptions.Events.UseClosedShapeStorage = true</c> and
/// <c>AppendMode = EventAppendMode.Rich</c>, the async-projection
/// side-effect replay path (JasperFx.Events <c>EventSlice.BuildOperations</c>
/// → <c>IProjectionBatch.QuickAppendEventWithVersion</c> →
/// Marten <c>ProjectionBatch.QuickAppendEventWithVersion</c> →
/// <c>ClosedShapeEventDocumentStorage</c> →
/// <c>RichEventStorage&lt;TId&gt;.QuickAppendEventWithVersion</c>) used to throw
/// <c>NotImplementedException</c>. The closed-shape Rich implementation was
/// stubbed out and the assumption "the Rich appender only calls AppendEvent"
/// missed the daemon's side-effect replay path. This test asserts the
/// raised side-effect event lands in <c>mt_events</c> when running the rebuild
/// under <c>UseClosedShapeStorage = true</c> + Rich.
/// </summary>
public class Bug_4428_rich_storage_side_effect_events: OneOffConfigurationsContext
{
    [Fact]
    public async Task raised_side_effect_event_persists_under_closed_shape_rich()
    {
        StoreOptions(opts =>
        {
            // Force the closed-shape Rich path explicitly so the test reproduces
            // #4428 regardless of the suite-wide env flag (which only affects
            // UseClosedShapeStorage, not AppendMode).
            opts.Events.UseClosedShapeStorage = true;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Projections.Add<Bug4428CounterProjection>(ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        // Five Increment events — projection raises a BonusAwarded side-effect
        // when the counter hits a multiple of 5.
        theSession.Events.StartStream<Bug4428Counter>(streamId,
            new Bug4428Incremented(),
            new Bug4428Incremented(),
            new Bug4428Incremented(),
            new Bug4428Incremented(),
            new Bug4428Incremented());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        // The raised side-effect event must have been persisted to mt_events
        // via RichEventStorage.QuickAppendEventWithVersion (closed-shape Rich
        // path). Before #4428 was fixed this call site threw
        // NotImplementedException; this test is the regression guard against
        // a future regression of that throw.
        //
        // We assert on the raw-events table directly rather than on the
        // projection snapshot's Bonuses counter — applying the raised event
        // back into the snapshot is a separate daemon re-poll pass that this
        // test isn't trying to exercise.
        var rawEvents = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.StreamId == streamId)
            .ToListAsync();
        rawEvents.OfType<IEvent<Bug4428BonusAwarded>>().Count().ShouldBe(1);
    }
}

public record Bug4428Incremented;

public record Bug4428BonusAwarded;

public class Bug4428Counter: IRevisioned
{
    public Guid Id { get; set; }
    public int Increments { get; set; }
    public int Bonuses { get; set; }
    public long Version { get; set; }
}

public class Bug4428CounterProjection: SingleStreamProjection<Bug4428Counter, Guid>
{
    public void Apply(Bug4428Counter counter, Bug4428Incremented _) => counter.Increments++;

    public void Apply(Bug4428Counter counter, Bug4428BonusAwarded _) => counter.Bonuses++;

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<Bug4428Counter> slice)
    {
        if (slice.Snapshot != null && slice.Snapshot.Increments > 0 && slice.Snapshot.Increments % 5 == 0)
        {
            slice.AppendEvent(new Bug4428BonusAwarded());
        }

        return new ValueTask();
    }
}
