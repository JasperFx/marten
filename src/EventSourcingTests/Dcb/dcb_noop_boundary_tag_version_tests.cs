#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// A session is allowed to fetch a DCB boundary, decide there is nothing to append, and save.
// Saving that decision should leave the mt_dcb_tag_version rows the query touched exactly as
// captured, so it neither conflicts with another session making the same no-op decision nor
// invalidates a concurrent session that does have events to append.
[Collection("OneOffs")]
public class dcb_noop_boundary_tag_version_tests: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentGraded>();

            opts.Events.RegisterTagType<StudentId>("student");
        });
    }

    [Fact]
    public async Task two_sessions_that_append_nothing_do_not_conflict()
    {
        ConfigureStore();
        var query = new EventTagQuery().Or<StudentId>(new StudentId(Guid.NewGuid()));
        await using var session1 = theStore.LightweightSession();
        await using var session2 = theStore.LightweightSession();
        await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await session2.SaveChangesAsync();

        await Should.NotThrowAsync(() => session1.SaveChangesAsync());
    }

    [Fact]
    public async Task a_session_that_appends_nothing_does_not_invalidate_a_concurrent_append()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session1 = theStore.LightweightSession();
        await using var session2 = theStore.LightweightSession();
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await session2.SaveChangesAsync();

        await Should.NotThrowAsync(() => session1.SaveChangesAsync());
    }
}
