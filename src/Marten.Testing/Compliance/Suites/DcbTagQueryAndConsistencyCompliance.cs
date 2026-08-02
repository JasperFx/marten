using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Shouldly;
using Xunit;

namespace JasperFx.Events.ComplianceTests;

#region sample_compliance_dcb_tag_type_definitions

// Strong-typed tag identifiers
public record StudentId(Guid Value);

public record CourseId(Guid Value);

#endregion

#region sample_compliance_dcb_domain_events

public record StudentEnrolled(string StudentName, string CourseName);

public record AssignmentSubmitted(string AssignmentName, int Score);

public record StudentDropped(string Reason);

#endregion

/// <summary>
/// Event carrying tag-typed properties, so the store can infer its tags without WithTag()
/// </summary>
public record StudentGraded(StudentId StudentId, CourseId CourseId, int Grade);

/// <summary>
/// Event with NO tag-typed properties -- tag inference must fail loudly
/// </summary>
public record SystemNotification(string Message);

#region sample_compliance_dcb_aggregate

public partial class StudentCourseEnrollment
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
/// Dynamic Consistency Boundary behavior: tagging events, querying and aggregating across streams
/// by tag, and the FetchForWritingByTags optimistic consistency check -- direct and batched.
/// </summary>
public abstract class DcbTagQueryAndConsistencyCompliance<TFixture, TOperations, TQuerySession>
    : EventStoreComplianceSuite<TFixture, TOperations, TQuerySession>
    where TFixture : EventStoreComplianceFixture<TOperations, TQuerySession>, new()
    where TOperations : TQuerySession, IStorageOperations
{
    #region sample_compliance_dcb_registering_tag_types

    private static readonly Action<ComplianceStoreConfig> _configuration = config =>
    {
        config.SchemaName = "compliance_dcb";

        config.AddEventType<StudentEnrolled>();
        config.AddEventType<AssignmentSubmitted>();
        config.AddEventType<StudentDropped>();
        config.AddEventType<StudentGraded>();

        // Each tag type gets its own table, and is associated with the aggregate it bounds
        config.RegisterTagType<StudentId>("student", typeof(StudentCourseEnrollment));
        config.RegisterTagType<CourseId>("course", typeof(StudentCourseEnrollment));

        config.LiveAggregation<StudentCourseEnrollment>();
    };

    #endregion

    /// <summary>
    /// Same as the standard configuration except CourseId is registered with no aggregate
    /// association, so events tagged only with a course have nowhere to route.
    /// </summary>
    private static readonly Action<ComplianceStoreConfig> _unroutedCourseTagConfiguration = config =>
    {
        config.SchemaName = "compliance_dcb";

        config.AddEventType<StudentEnrolled>();
        config.AddEventType<StudentGraded>();

        config.RegisterTagType<StudentId>("student", typeof(StudentCourseEnrollment));
        config.RegisterTagType<CourseId>("course");

        config.LiveAggregation<StudentCourseEnrollment>();
    };

    protected override Action<ComplianceStoreConfig> Configuration => _configuration;

    [Fact]
    public async Task can_query_events_by_single_tag()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = OpenSession();

        #region sample_compliance_dcb_tagging_events

        var enrolled = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        EventsFor(session).Append(streamId, enrolled);
        await SaveChangesAsync(session);

        #endregion

        #region sample_compliance_dcb_query_by_single_tag

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);

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

        await using var session = OpenSession();

        var e1 = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        e1.WithTag(student1, course);
        EventsFor(session).Append(Guid.NewGuid(), e1);

        var e2 = EventsFor(session).BuildEvent(new StudentEnrolled("Bob", "Math"));
        e2.WithTag(student2, course);
        EventsFor(session).Append(Guid.NewGuid(), e2);

        await SaveChangesAsync(session);

        #region sample_compliance_dcb_query_multiple_tags_or

        var query = new EventTagQuery()
            .Or<StudentId>(student1)
            .Or<StudentId>(student2);

        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);

        #endregion

        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task can_query_events_across_distinct_tag_types_with_or()
    {
        // The core DCB boundary case: events on different streams carry DIFFERENT single tags and the
        // query OR-combines distinct tag types. Each matching event carries only one of the queried
        // tag types -- a regression guard against an INNER JOIN that would require every event to
        // carry all queried tag types, collapsing this query to zero rows.
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var session = OpenSession();

        var enrolled = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId);
        EventsFor(session).Append(Guid.NewGuid(), enrolled);

        var submitted = EventsFor(session).BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(courseId);
        EventsFor(session).Append(Guid.NewGuid(), submitted);

        await SaveChangesAsync(session);

        var query = new EventTagQuery()
            .Or<StudentId>(studentId)
            .Or<CourseId>(courseId);

        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);
        events.Count.ShouldBe(2);
        events.ShouldContain(e => e.Data is StudentEnrolled);
        events.ShouldContain(e => e.Data is AssignmentSubmitted);
    }

    [Fact]
    public async Task can_query_events_by_tag_with_event_type_filter()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = OpenSession();

        var enrolled = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);

        var submitted = EventsFor(session).BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(studentId, courseId);

        EventsFor(session).Append(streamId, enrolled, submitted);
        await SaveChangesAsync(session);

        #region sample_compliance_dcb_query_by_event_type

        var query = new EventTagQuery()
            .Or<AssignmentSubmitted, StudentId>(studentId);

        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);

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

        await using var session = OpenSession();
        await AppendTaggedEventAsync(session, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId,
            courseId);

        var query = new EventTagQuery().Or<StudentId>(otherStudentId);
        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);
        events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task can_aggregate_events_by_tags()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = OpenSession();

        var enrolled = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);

        var submitted = EventsFor(session).BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(studentId, courseId);

        EventsFor(session).Append(streamId, enrolled, submitted);
        await SaveChangesAsync(session);

        #region sample_compliance_dcb_aggregate_by_tags

        var query = new EventTagQuery()
            .Or<StudentId>(studentId)
            .Or<CourseId>(courseId);

        var aggregate = await EventsFor(session).AggregateByTagsAsync<StudentCourseEnrollment>(query, Cancellation);

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

        await using var session = OpenSession();

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var aggregate = await EventsFor(session).AggregateByTagsAsync<StudentCourseEnrollment>(query, Cancellation);
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task can_fetch_for_writing_by_tags_happy_path()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var session1 = OpenSession();
        await AppendTaggedEventAsync(session1, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId,
            courseId);

        #region sample_compliance_dcb_fetch_for_writing_by_tags

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        // Current state -- Aggregate may be null when no events matched yet
        var aggregate = boundary.Aggregate;
        var lastSequence = boundary.LastSeenSequence;

        var assignment = EventsFor(session2).BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        // Throws DcbConcurrencyException if another session appended matching events since the read
        await SaveChangesAsync(session2);

        #endregion

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.StudentName.ShouldBe("Alice");
        boundary.Events.Count.ShouldBe(1);
        lastSequence.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task fetch_for_writing_appends_to_existing_tag_derived_stream_without_collision()
    {
        // Seed the student's stream using the tag value as the stream id, so the boundary's
        // tag-derived routing targets a PRE-EXISTING stream on save. A boundary that unconditionally
        // starts a new stream here throws an existing-stream-collision error.
        var studentId = new StudentId(Guid.NewGuid());

        await using var session1 = OpenSession();
        await AppendTaggedEventAsync(session1, studentId.Value, new StudentEnrolled("Alice", "Math"), studentId);

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        var submitted = EventsFor(session2).BuildEvent(new AssignmentSubmitted("HW1", 95));
        submitted.WithTag(studentId);
        boundary.AppendOne(submitted);

        await Should.NotThrowAsync(async () => await SaveChangesAsync(session2));

        await using var session3 = OpenSession();
        var events = await EventsFor(session3)
            .QueryByTagsAsync(new EventTagQuery().Or<StudentId>(studentId), Cancellation);
        events.ShouldContain(e => e.Data is AssignmentSubmitted);
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_detects_concurrency_violation()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, streamId, new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session1 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session1).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        // Another session appends a matching event BEFORE session 1 saves
        await using var session2 = OpenSession();
        await AppendTaggedEventAsync(session2, streamId, new AssignmentSubmitted("HW-conflict", 50), studentId,
            courseId);

        var assignment = EventsFor(session1).BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        await Should.ThrowAsync<DcbConcurrencyException>(async () => await SaveChangesAsync(session1));
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_no_violation_when_unrelated_events_appended()
    {
        var student1 = new StudentId(Guid.NewGuid());
        var student2 = new StudentId(Guid.NewGuid());
        var course = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), student1, course);

        await using var session1 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(student1);
        var boundary = await EventsFor(session1).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        // A different student's event must NOT conflict
        await using var session2 = OpenSession();
        await AppendTaggedEventAsync(session2, Guid.NewGuid(), new StudentEnrolled("Bob", "Math"), student2, course);

        var assignment = EventsFor(session1).BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(student1, course);
        boundary.AppendOne(assignment);

        await Should.NotThrowAsync(async () => await SaveChangesAsync(session1));
    }

    [Fact]
    public async Task events_across_multiple_streams_can_be_queried_by_tag()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var course1 = new CourseId(Guid.NewGuid());
        var course2 = new CourseId(Guid.NewGuid());

        await using var session = OpenSession();

        var enrolled1 = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled1.WithTag(studentId, course1);
        EventsFor(session).Append(Guid.NewGuid(), enrolled1);

        var enrolled2 = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Science"));
        enrolled2.WithTag(studentId, course2);
        EventsFor(session).Append(Guid.NewGuid(), enrolled2);

        await SaveChangesAsync(session);

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);

        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task query_events_ordered_by_sequence()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = OpenSession();

        var enrolled = EventsFor(session).BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);

        var hw1 = EventsFor(session).BuildEvent(new AssignmentSubmitted("HW1", 90));
        hw1.WithTag(studentId, courseId);

        var hw2 = EventsFor(session).BuildEvent(new AssignmentSubmitted("HW2", 85));
        hw2.WithTag(studentId, courseId);

        EventsFor(session).Append(streamId, enrolled, hw1, hw2);
        await SaveChangesAsync(session);

        var query = new EventTagQuery().Or<StudentId>(studentId);
        var events = await EventsFor(session).QueryByTagsAsync(query, Cancellation);

        events.Count.ShouldBe(3);
        events[0].Sequence.ShouldBeLessThan(events[1].Sequence);
        events[1].Sequence.ShouldBeLessThan(events[2].Sequence);
    }

    [Fact]
    public async Task fetch_for_writing_with_empty_result_still_enforces_consistency()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var session1 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session1).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        boundary.Aggregate.ShouldBeNull();
        boundary.Events.Count.ShouldBe(0);
        boundary.LastSeenSequence.ShouldBe(0);

        await using var session2 = OpenSession();
        await AppendTaggedEventAsync(session2, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId,
            courseId);

        var e = EventsFor(session1).BuildEvent(new StudentEnrolled("Alice", "Math"));
        e.WithTag(studentId, courseId);
        boundary.AppendOne(e);

        await Should.ThrowAsync<DcbConcurrencyException>(async () => await SaveChangesAsync(session1));
    }

    [Fact]
    public async Task can_fetch_for_writing_by_tags_via_batch_query()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var batch = CreateBatch(session2);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundaryTask = batch.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await batch.Execute(Cancellation);

        var boundary = await boundaryTask;
        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.StudentName.ShouldBe("Alice");
        boundary.Events.Count.ShouldBe(1);
        boundary.LastSeenSequence.ShouldBeGreaterThan(0);

        var assignment = EventsFor(session2).BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);
        await SaveChangesAsync(session2);
    }

    [Fact]
    public async Task batch_query_fetch_for_writing_by_tags_detects_concurrency_violation()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, streamId, new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session1 = OpenSession();
        var batch = CreateBatch(session1);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundaryTask = batch.FetchForWritingByTags<StudentCourseEnrollment>(query);
        await batch.Execute(Cancellation);
        var boundary = await boundaryTask;

        await using var session2 = OpenSession();
        await AppendTaggedEventAsync(session2, streamId, new AssignmentSubmitted("HW-conflict", 50), studentId,
            courseId);

        var assignment = EventsFor(session1).BuildEvent(new AssignmentSubmitted("HW1", 95));
        assignment.WithTag(studentId, courseId);
        boundary.AppendOne(assignment);

        await Should.ThrowAsync<DcbConcurrencyException>(async () => await SaveChangesAsync(session1));
    }

    #region sample_compliance_dcb_events_exist_async

    [Fact]
    public async Task events_exist_returns_true_when_matching_events_found()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var session = OpenSession();
        await AppendTaggedEventAsync(session, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId,
            courseId);

        // Existence check -- no event loading
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var exists = await EventsFor(session).EventsExistAsync(query, Cancellation);
        exists.ShouldBeTrue();
    }

    #endregion

    [Fact]
    public async Task events_exist_returns_false_when_no_matching_events()
    {
        var studentId = new StudentId(Guid.NewGuid());

        await using var session = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var exists = await EventsFor(session).EventsExistAsync(query, Cancellation);
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task events_exist_with_event_type_filter()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var session = OpenSession();
        await AppendTaggedEventAsync(session, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId,
            courseId);

        var query1 = new EventTagQuery().Or<StudentEnrolled, StudentId>(studentId);
        (await EventsFor(session).EventsExistAsync(query1, Cancellation)).ShouldBeTrue();

        // No AssignmentSubmitted was appended
        var query2 = new EventTagQuery().Or<AssignmentSubmitted, StudentId>(studentId);
        (await EventsFor(session).EventsExistAsync(query2, Cancellation)).ShouldBeFalse();
    }

    [Fact]
    public async Task events_exist_via_batch_query_positive()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var batch = CreateBatch(session2);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var existsTask = batch.EventsExist(query);
        await batch.Execute(Cancellation);

        (await existsTask).ShouldBeTrue();
    }

    [Fact]
    public async Task events_exist_via_batch_query_negative()
    {
        var studentId = new StudentId(Guid.NewGuid());

        await using var session = OpenSession();
        var batch = CreateBatch(session);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var existsTask = batch.EventsExist(query);
        await batch.Execute(Cancellation);

        (await existsTask).ShouldBeFalse();
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_throws_on_empty_query()
    {
        await using var session = OpenSession();

        var query = new EventTagQuery();
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await EventsFor(session).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);
        });
    }

    [Fact]
    public async Task append_event_with_inferred_tags_from_properties()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        // Raw event carrying StudentId and CourseId properties -- tags are inferred
        boundary.AppendOne(new StudentGraded(studentId, courseId, 95));

        await SaveChangesAsync(session2);

        await using var session3 = OpenSession();
        var events = await EventsFor(session3)
            .QueryByTagsAsync(new EventTagQuery().Or<StudentId>(studentId), Cancellation);
        events.Count.ShouldBe(2);
        events[1].Data.ShouldBeOfType<StudentGraded>().Grade.ShouldBe(95);
    }

    [Fact]
    public async Task append_event_with_no_tags_and_no_inferable_properties_throws()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        Should.Throw<InvalidOperationException>(() => boundary.AppendOne(new SystemNotification("test")));
    }

    [Fact]
    public async Task append_already_wrapped_event_with_explicit_tags_works()
    {
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        var graded = EventsFor(session2).BuildEvent(new StudentGraded(studentId, courseId, 88));
        graded.WithTag(studentId, courseId);
        boundary.AppendOne(graded);

        await Should.NotThrowAsync(async () => await SaveChangesAsync(session2));
    }

    [Fact]
    public async Task append_event_with_tag_having_no_aggregate_type_creates_new_stream()
    {
        await theFixture.ConfigureAsync(_unroutedCourseTagConfiguration);
        await theFixture.CleanEventDataAsync();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var seed = OpenSession();
        await AppendTaggedEventAsync(seed, Guid.NewGuid(), new StudentEnrolled("Alice", "Math"), studentId, courseId);

        await using var session2 = OpenSession();
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await EventsFor(session2).FetchForWritingByTags<StudentCourseEnrollment>(query, Cancellation);

        // CourseId has no AggregateType -- the unrouted tag creates a new stream per event
        var graded = EventsFor(session2).BuildEvent(new StudentGraded(studentId, courseId, 90));
        graded.WithTag(courseId);
        boundary.AppendOne(graded);

        await Should.NotThrowAsync(async () => await SaveChangesAsync(session2));
    }
}
