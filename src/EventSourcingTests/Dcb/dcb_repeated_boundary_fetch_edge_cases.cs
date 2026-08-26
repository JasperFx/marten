#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// Extra ground around #5280's row-collapsing rule, found while #5281 was open as a duplicate report
/// of it. <see cref="Bug_5280_repeated_boundary_fetch_tests"/> covers the reported shape — one query
/// fetched twice, two overlapping queries, and the oldest-capture-wins rule. These are the cases that
/// probing the bug turned up alongside it and that nothing else pins down.
/// </summary>
[Collection("OneOffs")]
public class dcb_repeated_boundary_fetch_edge_cases: OneOffConfigurationsContext
{
    private void ConfigureStore(DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<StudentGraded>();
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    [Fact]
    public async Task hstore_mode_collapses_repeated_rows_the_same_way()
    {
        ConfigureStore(DcbStorageMode.HStore);

        // The capture step and the assertion are shared by both storage modes, so this should never
        // have diverged -- but nothing was holding it that way. The #5280 tests all run on the
        // TagTables default, and a mode-specific regression here would read as an absent tag rather
        // than an error, which is the failure a consistency boundary must not have.
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session = theStore.LightweightSession();

        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task appending_through_both_of_two_boundaries_over_one_row()
    {
        ConfigureStore();

        // #5280's repro appends through the second boundary only. Appending through both is the case
        // where each boundary object has events of its own, so a fix that deduped by "last boundary
        // wins" rather than by row would still have something queued for the first one.
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session = theStore.LightweightSession();

        var first = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        var second = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        first.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 90));
        second.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task two_disjoint_boundaries_in_one_session_both_survive_the_collapse()
    {
        ConfigureStore();

        // The other side of collapsing: rows that are *not* duplicates must all still be asserted.
        // A collapse keyed on something coarser than (tag_table, tag_value) would quietly drop one
        // of these, and the loss would only show up as a boundary that stopped being enforced.
        var studentA = new StudentId(Guid.NewGuid());
        var studentB = new StudentId(Guid.NewGuid());
        await using var session = theStore.LightweightSession();

        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(studentA));
        var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(studentB));
        boundary.AppendOne(new StudentGraded(studentB, new CourseId(Guid.NewGuid()), 95));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task a_boundary_re_fetched_after_a_committed_save_is_still_enforced()
    {
        ConfigureStore();

        // dcb_noop_boundary_tag_version_tests covers re-fetching after a *no-op* save and asserts it
        // can append. This is the same idiom after a save that actually committed, and it asserts
        // the opposite half: the second read is a real boundary, so an interloper still has to be
        // caught. A stale captured row surviving the commit would silently overwrite that append.
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);

        await using var session = theStore.LightweightSession();
        var first = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        first.AppendOne(new StudentGraded(studentId, courseId, 90));
        await session.SaveChangesAsync();

        var second = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using (var interloper = theStore.LightweightSession())
        {
            var conflicting = interloper.Events.BuildEvent(new StudentGraded(studentId, courseId, 10));
            conflicting.WithTag(studentId);
            interloper.Events.StartStream(Guid.NewGuid(), conflicting);
            await interloper.SaveChangesAsync();
        }

        second.AppendOne(new StudentGraded(studentId, courseId, 95));

        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }
}
