using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using NpgsqlTypes;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

// Regression guard for https://github.com/JasperFx/marten/issues/4085 (originally
// JasperFx/wolverine#2053).
//
// On a SINGLE-TENANTED store, events on one stream can end up with disagreeing tenant_id
// values -- the reporter had `DEFAULT` and `Marten` on the same stream, written by
// Wolverine stamping appends inconsistently between IStartStream and plain
// IEnumerable<object> returns. The async daemon then grouped that stream per tenant and
// folded it into several partial aggregates, so an Apply saw a document with every
// property at its default, as though Create had never run.
//
// Only the async daemon was affected. Live and inline aggregation fold the same events
// correctly, which is exactly why this went unnoticed for as long as it did, and why this
// test drives the daemon rather than asserting on a live aggregate.
//
// The fix has two halves that only work together:
//
//   * Marten's SingleStreamProjection.BuildSlicer sets ForceSingleTenancy on the slicer
//     when EventGraph.TenancyStyle is Single. That has been here since the original fix.
//   * TenantedEventSlicer honours that flag on BOTH of its overloads. It did not:
//     SliceAsync(IReadOnlyList<IEvent>) respected it while SliceAsync(EventRange) ignored
//     it -- and the async daemon reaches only the EventRange one. So the flag was being
//     set on precisely the path that could not read it, and honoured on the path that
//     never needed it. Fixed upstream in JasperFx/jasperfx#721, shipped in JasperFx
//     2.58.0; this test fails on anything earlier.
//
// The rows are seeded by updating tenant_id directly rather than by reproducing the
// Wolverine append path. That is deliberate: the bug is "given rows in this state, the
// daemon must fold them as one aggregate", and how they reached that state is not
// Marten's concern and is fragile to reproduce. The reporter's own workaround was the
// mirror image of this UPDATE.
public class Bug_4085_async_projection_with_mixed_tenant_ids: BugIntegrationContext
{
    public record TallyIncremented(int Amount);

    // Additive on purpose. A stream folded once and a stream folded in two pieces differ
    // in these numbers; a last-write-wins aggregate would hide the split.
    public class MixedTenancyTally
    {
        public Guid Id { get; set; }
        public int Total { get; set; }
        public int EventCount { get; set; }

        public void Apply(TallyIncremented e)
        {
            Total += e.Amount;
            EventCount++;
        }
    }

    [Fact]
    public async Task async_projection_folds_one_stream_despite_disagreeing_tenant_ids()
    {
        StoreOptions(_ =>
        {
            // No conjoined tenancy: the store stays single-tenanted, which is the whole
            // precondition. Any tenant id on an event is incidental rather than meaningful.
            _.Projections.Snapshot<MixedTenancyTally>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<MixedTenancyTally>(streamId,
            new TallyIncremented(1), new TallyIncremented(2), new TallyIncremented(4));
        await theSession.SaveChangesAsync();

        await dirtyTheTenantIdsOnAllButTheFirstEvent(streamId);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        await using var query = theStore.QuerySession();
        var tally = await query.Query<MixedTenancyTally>().FirstOrDefaultAsync(x => x.Id == streamId);

        tally.ShouldNotBeNull();

        // Before the fix the daemon sliced this stream into two tenant groups, and the
        // document reflected only whichever group was applied last -- so both numbers came
        // up short rather than wrong in some exotic way.
        tally.EventCount.ShouldBe(3);
        tally.Total.ShouldBe(7);
    }

    /// <summary>
    /// Reproduce the reported table state: one stream whose events do not agree on tenant_id.
    /// </summary>
    private async Task dirtyTheTenantIdsOnAllButTheFirstEvent(Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var updated = await conn
            .CreateCommand(
                $"update {theStore.Events.DatabaseSchemaName}.mt_events set tenant_id = 'Marten' where stream_id = :stream and version > 1")
            .With("stream", streamId, NpgsqlDbType.Uuid)
            .ExecuteNonQueryAsync();

        // Guard the precondition. If a future change normalises tenant_id on write or read,
        // this test would still pass -- for a reason that has nothing to do with slicing --
        // and would quietly stop guarding #4085. Polecat and Fisher both turned out to
        // normalise here, which is why the shared compliance suite could not cover this and
        // it lives in Marten's own tests instead (JasperFx/jasperfx#727).
        updated.ShouldBe(2);
    }
}
