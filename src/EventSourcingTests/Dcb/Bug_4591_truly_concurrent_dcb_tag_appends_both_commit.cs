#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Dcb;

// #4591 regression. Pre-fix, AssertDcbConsistency emitted a `SELECT EXISTS (...)` over
// mt_events as a separate non-locking statement before the INSERTs, with the transaction
// at READ COMMITTED. Two concurrent fetch-and-save sessions both ran their EXISTS check
// before either committed, both saw no conflict, both committed → 5–7 of 8 racers
// committed when the contract demands exactly one.
//
// The fix (see DcbTagVersionTable + DcbTagVersionAssertion) replaces the predicate
// read with an UPDATE … WHERE version = $captured on a side table keyed by
// (tag_table, tag_value, tenant_id). The UPDATE acquires a row lock; the WHERE
// clause is the optimistic check; concurrent saves serialize on the row.
//
// Run-mode coverage:
//   - [Theory] over DcbStorageMode (HStore + TagTables) so the side-table fix is
//     verified for both physical-storage shapes, not just the HStore one in the
//     reporter's environment.
//   - Reporter's environment was specifically HStore + Conjoined + LightweightSession.
//     The Theory pins that variant and adds the TagTables variant alongside it.
[Collection("OneOffs")]
public class Bug_4591_truly_concurrent_dcb_tag_appends_both_commit: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4591_truly_concurrent_dcb_tag_appends_both_commit(ITestOutputHelper output)
    {
        _output = output;
    }

    private void ConfigureStore(DcbStorageMode storageMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            // Reporter's environment for HStore variant; the side-table fix has to
            // hold for both DCB storage modes either way.
            opts.Events.DcbStorageMode = storageMode;
            opts.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.RegisterTagType<StudentId>("student")
                .ForAggregate<StudentCourseEnrollment>();
            opts.Events.RegisterTagType<CourseId>("course")
                .ForAggregate<StudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }

    [Theory]
    [InlineData(DcbStorageMode.HStore)]
    [InlineData(DcbStorageMode.TagTables)]
    public async Task truly_concurrent_appends_sharing_a_tag_serialize_to_one_winner(DcbStorageMode storageMode)
    {
        ConfigureStore(storageMode);

        const int Racers = 8;
        const string TenantId = "acme";

        // The DCB *boundary* tag — shared by every racer. This is the tag value
        // the DCB consistency check queries against; the bug was a check-then-act
        // race on this tag.
        var courseId = new CourseId(Guid.NewGuid());

        // Each racer also carries its OWN StudentId. EventBoundary routes events
        // to a stream by the first tag with an AggregateType, so distinct
        // per-racer StudentIds mean distinct streams — the
        // (stream_id, version) unique constraint on mt_events does NOT serialize
        // them. The race must be caught by the DCB tag check alone.

        // Seed: one event under a DIFFERENT student stream but tagged with the
        // shared CourseId so lastSeenSequence > 0.
        var seedStudentId = new StudentId(Guid.NewGuid());
        var seedStreamId = seedStudentId.Value;
        await using (var seedSession = theStore.LightweightSession(TenantId))
        {
            var enrolled = seedSession.Events.BuildEvent(new StudentEnrolled("Seed", "Math"));
            enrolled.WithTag(seedStudentId, courseId);
            seedSession.Events.Append(seedStreamId, enrolled);
            await seedSession.SaveChangesAsync();
        }

        // Barrier-sync the fetch → save handoff. Every session fetches and then
        // awaits the same TCS; once all `Racers` sessions are past the fetch the
        // barrier completes and every session races into its SaveChangesAsync
        // simultaneously. The captured tag versions are therefore identical
        // across all racers.
        var allFetched = new TaskCompletionSource[Racers];
        for (var i = 0; i < Racers; i++) allFetched[i] = new TaskCompletionSource();
        var startSaves = new TaskCompletionSource();

        // Query is by the SHARED CourseId — that's the boundary the DCB check
        // must serialize.
        var query = new EventTagQuery().Or<CourseId>(courseId);

        var racerTasks = Enumerable.Range(0, Racers).Select(i => Task.Run(async () =>
        {
            // Each racer has its own StudentId → its own stream when routed
            // by EventBoundary.AppendOne.
            var racerStudentId = new StudentId(Guid.NewGuid());

            await using var session = theStore.LightweightSession(TenantId);
            var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

            allFetched[i].SetResult();
            await startSaves.Task;

            var append = session.Events.BuildEvent(new AssignmentSubmitted($"HW-{i}", 80 + i));
            // studentId first → routes to studentId-stream (distinct per racer);
            // courseId second → recorded against the shared boundary.
            append.WithTag(racerStudentId, courseId);
            boundary.AppendOne(append);

            try
            {
                await session.SaveChangesAsync();
                return (Index: i, Committed: true, Exception: (Exception?)null);
            }
            catch (DcbConcurrencyException ex)
            {
                return (Index: i, Committed: false, Exception: (Exception?)ex);
            }
        })).ToArray();

        await Task.WhenAll(allFetched.Select(t => t.Task));
        startSaves.SetResult();

        var results = await Task.WhenAll(racerTasks);

        var committed = results.Count(r => r.Committed);
        var throws = results.Count(r => r.Exception is DcbConcurrencyException);
        var otherErrors = results.Where(r => r.Exception is not null and not DcbConcurrencyException).ToList();

        _output.WriteLine($"DCB storage mode: {storageMode}");
        _output.WriteLine($"Truly-concurrent racers: {Racers}");
        _output.WriteLine($"  Committed:                {committed}");
        _output.WriteLine($"  DcbConcurrencyException:  {throws}");
        if (otherErrors.Count > 0)
        {
            _output.WriteLine($"  Other exceptions:         {otherErrors.Count}");
            foreach (var er in otherErrors)
            {
                _output.WriteLine($"    Racer {er.Index}: {er.Exception}");
            }
        }

        // Exactly one racer should commit; everyone else must observe the
        // DCB concurrency violation.
        committed.ShouldBe(1,
            $"Expected exactly one truly-concurrent append to commit; observed {committed} commits + {throws} DcbConcurrencyException throws (mode = {storageMode}).");
        throws.ShouldBe(Racers - 1);
        otherErrors.ShouldBeEmpty();
    }
}
