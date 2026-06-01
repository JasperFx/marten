#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Dcb;

// #4591 reproduction — does NOT ship as a passing regression test today;
// this is the research scaffolding. The current behavior is BROKEN
// (multiple truly-concurrent appends commit when only one should), so
// the test's "correct behavior" assertion fails. Marked Skip so CI
// stays green; unskip locally to observe the race.
//
// Repro design:
//   1. Seed one tagged event so there's an existing lastSeenSequence > 0.
//   2. Spin up N parallel sessions. Each one:
//        a. Opens a LightweightSession.
//        b. FetchForWritingByTags<StudentCourseEnrollment>(query) — captures
//           the same lastSeenSequence in every session because nothing has
//           committed yet between them.
//        c. Awaits a TaskCompletionSource barrier so every session has
//           finished its fetch before any session begins its save.
//        d. Builds a tagged AppendOne carrying the conflicting tag value.
//        e. Calls SaveChangesAsync — this is where the AssertDcbConsistency
//           SELECT races against the other sessions' INSERTs at READ COMMITTED.
//   3. Tally successes vs DcbConcurrencyException throws across all N
//      sessions.
//
// Expected (post-fix): exactly 1 success, N-1 throws. Two truly-concurrent
//   conflicting appends should serialize.
// Actual (today): multiple successes — typically all N. Two transactions
//   both run their EXISTS SELECT before either commits, both see no conflict,
//   both insert.
[Collection("OneOffs")]
public class Bug_4591_truly_concurrent_dcb_tag_appends_both_commit: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4591_truly_concurrent_dcb_tag_appends_both_commit(ITestOutputHelper output)
    {
        _output = output;
    }

    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();

            // Reporter's environment: HStore tag storage + Conjoined tenancy.
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.RegisterTagType<StudentId>("student")
                .ForAggregate<StudentCourseEnrollment>();
            opts.Events.RegisterTagType<CourseId>("course")
                .ForAggregate<StudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }

    // Re-skip on master — the assertion FAILS on current code (8 racers
    // typically observe 5-7 commits + 1-3 throws). Unskip locally to
    // observe the race; un-skip permanently in the same PR that ships
    // the fix so it becomes a passing regression test.
    [Fact(Skip = "Reproduction for #4591 — fails on current code by design. See file-level comment + the research notes in PR.")]
    public async Task two_truly_concurrent_appends_sharing_a_tag_should_not_both_commit()
    {
        ConfigureStore();

        const int Racers = 8;
        const string TenantId = "acme";

        // The DCB *boundary* tag — shared by every racer. This is the tag
        // value the DCB consistency check queries against. The bug is on
        // this tag's check-then-act race.
        var courseId = new CourseId(Guid.NewGuid());

        // Each racer also carries its OWN StudentId. Marten's EventBoundary
        // routes events to a stream by the first tag with an AggregateType
        // — with WithTag(studentId, courseId), the streamId becomes
        // studentId.Value. Distinct per-racer StudentIds therefore mean
        // each racer writes to a DIFFERENT stream, so the
        // (stream_id, version) unique constraint on mt_events does NOT
        // serialize them. The race must be caught by the DCB tag check
        // alone — which is exactly what the bug is about.

        // Seed: one event under a DIFFERENT student stream but tagged with
        // the shared CourseId so lastSeenSequence > 0.
        var seedStudentId = new StudentId(Guid.NewGuid());
        var seedStreamId = seedStudentId.Value;
        await using (var seedSession = theStore.LightweightSession(TenantId))
        {
            var enrolled = seedSession.Events.BuildEvent(new StudentEnrolled("Seed", "Math"));
            enrolled.WithTag(seedStudentId, courseId);
            seedSession.Events.Append(seedStreamId, enrolled);
            await seedSession.SaveChangesAsync();
        }

        // Barrier-sync the fetch → save handoff. Every session fetches and
        // then awaits the same TCS; once all `Racers` sessions are past the
        // fetch, the barrier completes and every session races into its
        // SaveChangesAsync simultaneously. The lastSeenSequence captured by
        // each fetch is therefore identical across all racers.
        var allFetched = new TaskCompletionSource[Racers];
        for (var i = 0; i < Racers; i++) allFetched[i] = new TaskCompletionSource();
        var startSaves = new TaskCompletionSource();

        // Query is by the SHARED CourseId — that's the boundary the DCB
        // check serializes (or should serialize).
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
            // courseId second → recorded in mt_event_tag_course (shared, the
            // racing tag).
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

        _output.WriteLine($"Truly-concurrent racers: {Racers}");
        _output.WriteLine($"  Committed:                {committed}");
        _output.WriteLine($"  DcbConcurrencyException:  {throws}");

        // The contract: at most ONE racer should commit. Today's code commits
        // every racer (race window is wide open at READ COMMITTED with a
        // separate EXISTS SELECT before the inserts).
        committed.ShouldBe(1,
            $"Expected exactly one truly-concurrent append to commit; observed {committed} commits + {throws} DcbConcurrencyException throws. " +
            $"Each racer fetched with the same lastSeenSequence then raced its SaveChangesAsync — the AssertDcbConsistency SELECT runs as a separate non-locking statement before the INSERTs at READ COMMITTED, so concurrent SELECTs all see no conflict.");
    }
}
