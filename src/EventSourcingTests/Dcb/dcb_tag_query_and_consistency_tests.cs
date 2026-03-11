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
using Marten.Services.BatchQuerying;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

#region sample_marten_dcb_tag_type_definitions
// Strong-typed tag identifiers
public record StudentId(Guid Value);
public record CourseId(Guid Value);
#endregion

#region sample_marten_dcb_domain_events
// Domain events
public record StudentEnrolled(string StudentName, string CourseName);
public record AssignmentSubmitted(string AssignmentName, int Score);
public record StudentDropped(string Reason);
#endregion

// Event with tag-typed properties for inference testing
public record StudentGraded(StudentId StudentId, CourseId CourseId, int Grade);

// Event with NO tag-typed properties — should fail inference
public record SystemNotification(string Message);

#region sample_marten_dcb_aggregate
// Aggregate for DCB
public class StudentCourseEnrollment
{
    public Guid Id { get; set; }
    public string StudentName { get; set; } = "";
    public string CourseName { get; set; } = "";
    public List<string> Assignments { get; set; } = new();
    public bool IsDropped { get; set; }

    public void Apply(StudentEnrolled e)
    {
        StudentName = e.StudentName;
        CourseName = e.CourseName;
    }

    public void Apply(AssignmentSubmitted e)
    {
        Assignments.Add(e.AssignmentName);
    }

    public void Apply(StudentDropped e)
    {
        IsDropped = true;
    }
}
#endregion

[Collection("OneOffs")]
public class dcb_tag_query_and_consistency_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    #region sample_marten_dcb_registering_tag_types
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();
            opts.Events.AddEventType<StudentDropped>();
            opts.Events.AddEventType<StudentGraded>();

            // Register tag types -- each gets its own table (mt_event_tag_student, mt_event_tag_course)
            opts.Events.RegisterTagType<StudentId>("student")
                .ForAggregate<StudentCourseEnrollment>();
            opts.Events.RegisterTagType<CourseId>("course")
                .ForAggregate<StudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }
    #endregion

    public Task InitializeAsync()
    {
        ConfigureStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task AppendTaggedEvent(Guid streamId, object eventData, params object[] tags)
    {
        var wrapped = theSession.Events.BuildEvent(eventData);
        wrapped.WithTag(tags);
        theSession.Events.Append(streamId, wrapped);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task can_query_events_by_single_tag()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        #region sample_marten_dcb_tagging_events
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();
        #endregion

        #region sample_marten_dcb_query_by_single_tag
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await theSession.Events.QueryByTagsAsync(query);
        #endregion

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
    }

    [Fact]
    public async Task can_query_events_by_multiple_tags_with_or()
    {
        var student1 = new StudentId(Guid.NewGuid());
        var student2 = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        // Student 1 enrolled
        var e1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        e1.WithTag(student1, course);
        theSession.Events.Append(stream1, e1);

        // Student 2 enrolled
        var e2 = theSession.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        e2.WithTag(student2, course);
        theSession.Events.Append(stream2, e2);

        await theSession.SaveChangesAsync();

        #region sample_marten_dcb_query_multiple_tags_or
        // Query for either student
        var query = new EventTagQuery()
            .Or<StudentId>(student1)
            .Or<StudentId>(student2);

        var events = await theSession.Events.QueryByTagsAsync(query);
        #endregion
        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task can_query_events_by_tag_with_event_type_filter()
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

        #region sample_marten_dcb_query_by_event_type
        // Query only AssignmentSubmitted events for this student
        var query = new EventTagQuery()
            .Or<AssignmentSubmitted, StudentId>(studentId);

        var events = await theSession.Events.QueryByTagsAsync(query);
        #endregion
        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<AssignmentSubmitted>().AssignmentName.ShouldBe("HW1");
    }

    [Fact]
    public async Task query_returns_empty_when_no_matching_tags()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var otherStudentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Query for a different student
        var query = new EventTagQuery().Or<StudentId>(otherStudentId);
        var events = await theSession.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task can_aggregate_events_by_tags()
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

        #region sample_marten_dcb_aggregate_by_tags
        var query = new EventTagQuery()
            .Or<StudentId>(studentId)
            .Or<CourseId>(courseId);

        var aggregate = await theSession.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
        #endregion
        aggregate.ShouldNotBeNull();
        aggregate.StudentName.ShouldBe("Alice");
        aggregate.CourseName.ShouldBe("Math");
        aggregate.Assignments.ShouldContain("HW1");
    }

    [Fact]
    public async Task aggregate_by_tags_returns_null_when_no_events()
    {
        var studentId = new StudentId(Guid.NewGuid());

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var aggregate = await theSession.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task can_fetch_for_writing_by_tags_happy_path()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Seed initial events
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        #region sample_marten_dcb_fetch_for_writing_by_tags
        // Fetch for writing
        await using var session2 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Read current state
        var aggregate = boundary.Aggregate; // may be null if no events yet
        var lastSequence = boundary.LastSeenSequence;

        // Append via boundary
        var assignment = session2.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        // Save -- will throw DcbConcurrencyException if another session
        // appended matching events after our read
        await session2.SaveChangesAsync();
        #endregion

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.StudentName.ShouldBe("Alice");
        boundary.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_detects_concurrency_violation()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Seed initial events
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Session 1: fetch for writing
        await using var session1 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Session 2: append a conflicting event BEFORE session 1 saves
        await using var session2 = theStore.LightweightSession();
        var conflicting = session2.Events.BuildEvent(new AssignmentSubmitted("HW-conflict", 50));
        conflicting.WithTag(studentId, courseId);
        session2.Events.Append(streamId, conflicting);
        await session2.SaveChangesAsync();

        // Session 1: try to save — should throw DcbConcurrencyException
        var assignment = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        #region sample_marten_dcb_handling_concurrency
        try
        {
            await session1.SaveChangesAsync();
        }
        catch (DcbConcurrencyException ex)
        {
            // Reload and retry -- the boundary's tag query had new matching events
            // ex.Query -- the original tag query
            // ex.LastSeenSequence -- the sequence at time of read
        }
        #endregion
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_no_violation_when_unrelated_events_appended()
    {
        var student1 = new StudentId(Guid.NewGuid());
        var student2 = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        // Seed student1
        var enrolled1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled1.WithTag(student1, course);
        theSession.Events.Append(stream1, enrolled1);
        await theSession.SaveChangesAsync();

        // Session 1: fetch for writing for student1
        await using var session1 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(student1);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Session 2: append event for DIFFERENT student — should NOT conflict
        await using var session2 = theStore.LightweightSession();
        var enrolled2 = session2.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        enrolled2.WithTag(student2, course);
        session2.Events.Append(stream2, enrolled2);
        await session2.SaveChangesAsync();

        // Session 1: save should succeed
        var assignment = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(student1, course);
        boundary.AppendOne(assignment);

        await session1.SaveChangesAsync(); // Should not throw
    }

    [Fact]
    public async Task events_across_multiple_streams_can_be_queried_by_tag()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var course1 = new CourseId(Guid.NewGuid());
        var course2 = new CourseId(Guid.NewGuid());
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        // Student enrolled in two courses (different streams)
        var enrolled1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled1.WithTag(studentId, course1);
        theSession.Events.Append(stream1, enrolled1);

        var enrolled2 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Science"));
        enrolled2.WithTag(studentId, course2);
        theSession.Events.Append(stream2, enrolled2);

        await theSession.SaveChangesAsync();

        // Query all events for this student across streams
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await theSession.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task query_events_ordered_by_sequence()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);

        var hw1 = theSession.Events.BuildEvent(new AssignmentSubmitted("HW1", 90));
        hw1.WithTag(studentId, courseId);

        var hw2 = theSession.Events.BuildEvent(new AssignmentSubmitted("HW2", 85));
        hw2.WithTag(studentId, courseId);

        theSession.Events.Append(streamId, enrolled, hw1, hw2);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await theSession.Events.QueryByTagsAsync(query);

        events.Count.ShouldBe(3);
        // Events should be ordered by sequence
        events[0].Sequence.ShouldBeLessThan(events[1].Sequence);
        events[1].Sequence.ShouldBeLessThan(events[2].Sequence);
    }

    [Fact]
    public async Task fetch_for_writing_with_empty_result_still_enforces_consistency()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        // Fetch for writing when no events exist
        await using var session1 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        boundary.Aggregate.ShouldBeNull();
        boundary.Events.Count.ShouldBe(0);
        boundary.LastSeenSequence.ShouldBe(0);

        // Another session appends a matching event before save
        await using var session2 = theStore.LightweightSession();
        var enrolled = session2.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        var streamId = Guid.NewGuid();
        session2.Events.Append(streamId, enrolled);
        await session2.SaveChangesAsync();

        // Session 1 tries to save — should detect the new matching event
        var e = session1.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        e.WithTag(studentId, courseId);
        boundary.AppendOne(e);

        await Should.ThrowAsync<DcbConcurrencyException>(async () =>
        {
            await session1.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task can_fetch_for_writing_by_tags_via_batch_query()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var batch = session2.CreateBatchQuery();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundaryTask = batch.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await batch.Execute();

        var boundary = await boundaryTask;
        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.StudentName.ShouldBe("Alice");
        boundary.Events.Count.ShouldBe(1);
        boundary.LastSeenSequence.ShouldBeGreaterThan(0);

        // Append via boundary and save
        var assignment = session2.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);
        await session2.SaveChangesAsync();
    }

    [Fact]
    public async Task batch_query_fetch_for_writing_by_tags_detects_concurrency_violation()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Session 1: fetch via batch query
        await using var session1 = theStore.LightweightSession();
        var batch = session1.CreateBatchQuery();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundaryTask = batch.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await batch.Execute();
        var boundary = await boundaryTask;

        // Session 2: append conflicting event
        await using var session2 = theStore.LightweightSession();
        var conflicting = session2.Events.BuildEvent(new AssignmentSubmitted("HW-conflict", 50));
        conflicting.WithTag(studentId, courseId);
        session2.Events.Append(streamId, conflicting);
        await session2.SaveChangesAsync();

        // Session 1: try to save — should throw
        var assignment = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        await Should.ThrowAsync<DcbConcurrencyException>(async () =>
        {
            await session1.SaveChangesAsync();
        });
    }

    #region sample_marten_dcb_events_exist_async
    [Fact]
    public async Task events_exist_returns_true_when_matching_events_found()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Check existence -- lightweight, no event loading
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var exists = await theSession.Events.EventsExistAsync(query);
        exists.ShouldBeTrue();
    }
    #endregion

    [Fact]
    public async Task events_exist_returns_false_when_no_matching_events()
    {
        var studentId = new StudentId(Guid.NewGuid());

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var exists = await theSession.Events.EventsExistAsync(query);
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task events_exist_with_event_type_filter()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Should find StudentEnrolled
        var query1 = new EventTagQuery().Or<StudentEnrolled, StudentId>(studentId);
        (await theSession.Events.EventsExistAsync(query1)).ShouldBeTrue();

        // Should NOT find AssignmentSubmitted (none appended)
        var query2 = new EventTagQuery().Or<AssignmentSubmitted, StudentId>(studentId);
        (await theSession.Events.EventsExistAsync(query2)).ShouldBeFalse();
    }

    [Fact]
    public async Task events_exist_via_batch_query_positive()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        var batch = session2.CreateBatchQuery();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var existsTask = batch.Events.EventsExist(query);
        await batch.Execute();

        (await existsTask).ShouldBeTrue();
    }

    [Fact]
    public async Task events_exist_via_batch_query_negative()
    {
        var studentId = new StudentId(Guid.NewGuid());

        await using var session2 = theStore.LightweightSession();
        var batch = session2.CreateBatchQuery();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var existsTask = batch.Events.EventsExist(query);
        await batch.Execute();

        (await existsTask).ShouldBeFalse();
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_throws_on_empty_query()
    {
        var query = new EventTagQuery();
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await theSession.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        });
    }

    [Fact]
    public async Task append_event_with_inferred_tags_from_properties()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Seed initial event with explicit tags
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Fetch for writing
        await using var session2 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Append a raw event that has StudentId and CourseId properties —
        // tags should be inferred automatically
        boundary.AppendOne(new StudentGraded(studentId, courseId, 95));

        // Should succeed — tags inferred from properties
        await session2.SaveChangesAsync();

        // Verify the event is discoverable by tag query
        await using var session3 = theStore.LightweightSession();
        var events = await session3.Events.QueryByTagsAsync(
            new EventTagQuery().Or<StudentId>(studentId));
        events.Count.ShouldBe(2);
        events[1].Data.ShouldBeOfType<StudentGraded>().Grade.ShouldBe(95);
    }

    [Fact]
    public async Task append_event_with_no_tags_and_no_inferable_properties_throws()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Seed initial event
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Fetch for writing
        await using var session2 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Append an event with no tags and no tag-typed properties — should throw
        Should.Throw<InvalidOperationException>(() =>
        {
            boundary.AppendOne(new SystemNotification("test"));
        });
    }

    [Fact]
    public async Task append_already_wrapped_event_with_explicit_tags_works()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // Seed initial event
        var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        theSession.Events.Append(streamId, enrolled);
        await theSession.SaveChangesAsync();

        // Fetch for writing
        await using var session2 = theStore.LightweightSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        // Append an already-wrapped event with explicit tags
        var graded = session2.Events.BuildEvent(new StudentGraded(studentId, courseId, 88));
        graded.WithTag(studentId, courseId);
        boundary.AppendOne(graded);

        await session2.SaveChangesAsync();
    }

    [Fact]
    public async Task append_event_with_tag_having_no_aggregate_type_creates_new_stream()
    {
        // Register a tag type WITHOUT an aggregate association
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<StudentGraded>();

            opts.Events.RegisterTagType<StudentId>("student");
            // CourseId registered WITHOUT ForAggregate
            opts.Events.RegisterTagType<CourseId>("course");

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });

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

        // CourseId tag has no AggregateType — should create a new stream per event
        var graded = session2.Events.BuildEvent(new StudentGraded(studentId, courseId, 90));
        graded.WithTag(courseId);
        boundary.AppendOne(graded);

        // Should succeed — unrouted tag creates a new stream
        await session2.SaveChangesAsync();
    }
}
