#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Dcb;
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

/// <summary>
/// The executable source of the DCB documentation samples in <c>docs/events/dcb.md</c>, and the
/// home of the tag/event/aggregate types the other Marten-specific DCB test fixtures share.
/// </summary>
/// <remarks>
/// The behavioral coverage that used to live here now runs once in
/// <see cref="JasperFx.Events.ComplianceTests.DcbTagQueryAndConsistencyCompliance{TFixture,TOperations,TQuerySession}"/>
/// against every Critter Stack event store. What stays behind is the documentation: each test below
/// backs a <c>sample_marten_dcb_*</c> snippet block, so it has to keep compiling and passing with
/// Marten-flavored API calls in it.
/// </remarks>
[Collection("OneOffs")]
public class dcb_documentation_samples: OneOffConfigurationsContext, IAsyncLifetime
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

    public override ValueTask InitializeAsync()
    {
        ConfigureStore();
        return default;
    }

    public override ValueTask DisposeAsync() => base.DisposeAsync();

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

        var e1 = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        e1.WithTag(student1, course);
        theSession.Events.Append(Guid.NewGuid(), e1);

        var e2 = theSession.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        e2.WithTag(student2, course);
        theSession.Events.Append(Guid.NewGuid(), e2);

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
}
