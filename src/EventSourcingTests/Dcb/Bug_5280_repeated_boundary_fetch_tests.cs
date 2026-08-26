#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// #5280: a save asserts each mt_dcb_tag_version row it captured exactly once. Reading one row
// through two boundaries -- the same query fetched twice, or two queries that overlap on a tag --
// used to queue two assertions carrying the same captured version, and the first one's bump made the
// second one's `where version = $captured` miss. The session reported a concurrency conflict against
// itself.
[Collection("OneOffs")]
public class Bug_5280_repeated_boundary_fetch_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentGraded>();

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });
    }

    [Fact]
    public async Task the_same_boundary_fetched_twice_can_still_append()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session = theStore.LightweightSession();

        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task two_boundaries_overlapping_on_one_tag_can_still_append()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        await using var session = theStore.LightweightSession();

        // Both queries name the student row; only the second names the course row.
        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(studentId));
        var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(studentId).Or<CourseId>(courseId));
        boundary.AppendOne(new StudentGraded(studentId, courseId, 95));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    // Collapsing the duplicate must not collapse the check itself.
    [Fact]
    public async Task a_twice_fetched_boundary_is_still_checked_against_a_concurrent_append()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);

        await using var session1 = theStore.LightweightSession();
        await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        var boundary1 = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using (var session2 = theStore.LightweightSession())
        {
            var boundary2 = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
            boundary2.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 60));
            await session2.SaveChangesAsync();
        }

        boundary1.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        var ex = await Should.ThrowAsync<DcbConcurrencyException>(() => session1.SaveChangesAsync());
        ex.Query.ShouldBe(query);
    }

    // The strictest of the captured versions wins: the first read is the one the caller may have
    // reasoned from, so a boundary that moved between two fetches is still a conflict.
    [Fact]
    public async Task the_oldest_capture_of_a_row_is_the_one_enforced()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);

        await using var session1 = theStore.LightweightSession();
        await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Another session moves the boundary in between session1's two reads
        await using (var session2 = theStore.LightweightSession())
        {
            var boundary2 = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
            boundary2.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 60));
            await session2.SaveChangesAsync();
        }

        var boundary1 = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary1.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await Should.ThrowAsync<DcbConcurrencyException>(() => session1.SaveChangesAsync());
    }
}
