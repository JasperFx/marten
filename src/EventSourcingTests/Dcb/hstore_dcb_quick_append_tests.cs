#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// Parallel of <see cref="dcb_quick_append_tests"/> with
/// <see cref="DcbStorageMode.HStore"/> enabled. The Quick append path is
/// codegen-generated and goes through <c>mt_quick_append_events</c>; in HStore
/// mode the function signature is trimmed (no per-tag varchar[] params) and
/// tags are written via a follow-up <c>SetEventTagsHstoreByEventIdOperation</c>
/// UPDATE. These tests exercise that path end-to-end.
/// </summary>
[Collection("OneOffs")]
public class hstore_dcb_quick_append_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();
            opts.Events.AddEventType<StudentDropped>();

            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");

            opts.Events.AppendMode = EventAppendMode.Quick;

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }

    public Task InitializeAsync()
    {
        ConfigureStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task can_query_events_by_single_tag_with_quick_append()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await theSession.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    [Fact]
    public async Task can_query_events_by_multiple_tags_with_quick_append()
    {
        var student1 = new StudentId(Guid.NewGuid());
        var student2 = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        var e1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        e1.WithTag(student1, course);
        theSession.Events.Append(stream1, e1);

        var e2 = theSession.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        e2.WithTag(student2, course);
        theSession.Events.Append(stream2, e2);

        await theSession.SaveChangesAsync();

        var query = new EventTagQuery()
            .Or<StudentId>(student1)
            .Or<StudentId>(student2);

        var events = await theSession.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task can_aggregate_events_by_tags_with_quick_append()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);

        var submitted = theSession.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(studentId, courseId);

        theSession.Events.Append(streamId, enrolled, submitted);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery()
            .Or<StudentId>(studentId)
            .Or<CourseId>(courseId);

        var aggregate = await theSession.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
        aggregate.ShouldNotBeNull();
        aggregate.StudentName.ShouldBe("Alice");
        aggregate.CourseName.ShouldBe("Math");
        aggregate.Assignments.ShouldContain("HW1");
    }

    [Fact]
    public async Task can_fetch_for_writing_by_tags_with_quick_append()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.StudentName.ShouldBe("Alice");
        boundary.Events.Count.ShouldBe(1);
        boundary.LastSeenSequence.ShouldBeGreaterThan(0);

        var assignment = session2.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        await session2.SaveChangesAsync();
    }

    [Fact]
    public async Task fetch_for_writing_detects_concurrency_violation_with_quick_append()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        await using var session1 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using var session2 = theStore.LightweightSession();
        var conflicting = session2.Events.BuildEvent(new AssignmentSubmitted("HW-conflict", 50));
        conflicting.WithTag(studentId, courseId);
        session2.Events.Append(streamId, conflicting);
        await session2.SaveChangesAsync();

        var assignment = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        await Should.ThrowAsync<DcbConcurrencyException>(() => session1.SaveChangesAsync());
    }

    [Fact]
    public async Task fetch_for_writing_no_violation_when_unrelated_events_with_quick_append()
    {
        var student1 = new StudentId(Guid.NewGuid());
        var student2 = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        var enrolled1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled1.WithTag(student1, course);
        theSession.Events.Append(stream1, enrolled1);
        await theSession.SaveChangesAsync();

        await using var session1 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(student1);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using var session2 = theStore.LightweightSession();
        var enrolled2 = session2.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        enrolled2.WithTag(student2, course);
        session2.Events.Append(stream2, enrolled2);
        await session2.SaveChangesAsync();

        var assignment = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(student1, course);
        boundary.AppendOne(assignment);

        await session1.SaveChangesAsync();
    }

    [Fact]
    public async Task events_across_multiple_streams_queried_by_tag_with_quick_append()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var course1 = new CourseId(Guid.NewGuid());
        var course2 = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        var enrolled1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled1.WithTag(studentId, course1);
        theSession.Events.Append(stream1, enrolled1);

        var enrolled2 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Science"));
        enrolled2.WithTag(studentId, course2);
        theSession.Events.Append(stream2, enrolled2);

        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await theSession.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(2);
    }
}
