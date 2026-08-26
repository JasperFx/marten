#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Testing.Harness;
using Npgsql;
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

    [Fact]
    public async Task a_session_that_appends_nothing_leaves_the_tag_version_row_alone()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);

        // Seed the row so this covers the ON CONFLICT branch as well as the missing-row one
        await using (var seed = theStore.LightweightSession())
        {
            var boundary = await seed.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
            boundary.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 70));
            await seed.SaveChangesAsync();
        }

        var before = await CurrentTagVersionAsync(studentId);
        before.ShouldNotBeNull();

        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await session.SaveChangesAsync();

        (await CurrentTagVersionAsync(studentId)).ShouldBe(before);
    }

    // A no-op decision writes nothing, so there is nothing for the boundary to guard -- a concurrent
    // append that moved the boundary underneath it is not a conflict, it is simply news the session
    // never acted on.
    [Fact]
    public async Task a_session_that_appends_nothing_does_not_throw_when_the_boundary_moved()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session1 = theStore.LightweightSession();
        await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using (var session2 = theStore.LightweightSession())
        {
            var boundary2 = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
            boundary2.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 88));
            await session2.SaveChangesAsync();
        }

        await Should.NotThrowAsync(() => session1.SaveChangesAsync());
    }

    // The retry idiom: a no-op save, then a fresh boundary on the same session. The first read must
    // not still be pending, or its captured version would collide with the second one's.
    [Fact]
    public async Task a_boundary_re_fetched_after_a_no_op_save_can_append()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session = theStore.LightweightSession();

        await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await session.SaveChangesAsync();

        var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 91));

        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    // The other half of the rule: a session that DOES append is still held to the boundary it read.
    [Fact]
    public async Task a_session_that_appends_is_still_checked_against_the_boundary()
    {
        ConfigureStore();
        var studentId = new StudentId(Guid.NewGuid());
        var query = new EventTagQuery().Or<StudentId>(studentId);
        await using var session1 = theStore.LightweightSession();
        var boundary1 = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using (var session2 = theStore.LightweightSession())
        {
            var boundary2 = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
            boundary2.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 88));
            await session2.SaveChangesAsync();
        }

        boundary1.AppendOne(new StudentGraded(studentId, new CourseId(Guid.NewGuid()), 95));

        await Should.ThrowAsync<DcbConcurrencyException>(() => session1.SaveChangesAsync());
    }

    private async Task<long?> CurrentTagVersionAsync(StudentId studentId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select version from {SchemaName}.mt_dcb_tag_version where tag_table = 'student' and tag_value = @value";
        cmd.Parameters.AddWithValue("value", studentId.Value.ToString());

        var raw = await cmd.ExecuteScalarAsync();
        return raw == null || raw == DBNull.Value ? null : Convert.ToInt64(raw);
    }
}
