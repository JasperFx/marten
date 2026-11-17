#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// The read-only DCB tag queries (<c>EventsExistAsync</c>, <c>QueryByTagsAsync</c> and
/// <c>AggregateByTagsAsync</c>) are on <see cref="Marten.Events.IQueryEventStore"/>, so they work
/// through a plain <see cref="IQuerySession"/> as they do through a document session. Each scenario
/// tags events on two streams, as a record built from several streams is the reason to read by tag.
/// </summary>
[Collection("OneOffs")]
public class dcb_tag_queries_on_a_query_session: OneOffConfigurationsContext
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private void ConfigureStore(DcbStorageMode mode = DcbStorageMode.TagTables,
        TenancyStyle tenancy = TenancyStyle.Single)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = tenancy;
            opts.Events.DcbStorageMode = mode;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();
            opts.Events.AddEventType<StudentDropped>();

            opts.Events.RegisterTagType<StudentId>("student")
                .ForAggregate<StudentCourseEnrollment>();
            opts.Events.RegisterTagType<CourseId>("course")
                .ForAggregate<StudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }

    private async Task AppendOnTwoStreams(StudentId studentId, CourseId courseId, string studentName,
        string assignment, string? tenantId = null)
    {
        await using var session = tenantId == null
            ? theStore.LightweightSession()
            : theStore.LightweightSession(tenantId);

        var enrolled = session.Events.BuildEvent(new StudentEnrolled(studentName, "Math"));
        enrolled.WithTag(studentId, courseId);
        session.Events.Append(Guid.NewGuid(), enrolled);

        var submitted = session.Events.BuildEvent(new AssignmentSubmitted(assignment, 90));
        submitted.WithTag(studentId, courseId);
        session.Events.Append(Guid.NewGuid(), submitted);

        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task query_by_tags_through_a_query_session(DcbStorageMode mode)
    {
        ConfigureStore(mode);
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        await AppendOnTwoStreams(studentId, courseId, "Alice", "HW1");

        await using IQuerySession query = theStore.QuerySession();
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or<StudentId>(studentId));

        events.Count.ShouldBe(2);
        events[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");
        events[1].Data.ShouldBeOfType<AssignmentSubmitted>().AssignmentName.ShouldBe("HW1");
        events[0].StreamId.ShouldNotBe(events[1].StreamId);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task aggregate_by_tags_through_a_query_session(DcbStorageMode mode)
    {
        ConfigureStore(mode);
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        await AppendOnTwoStreams(studentId, courseId, "Alice", "HW1");

        await using IQuerySession query = theStore.QuerySession();
        var enrollment = await query.Events.AggregateByTagsAsync<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(studentId).Or<CourseId>(courseId));

        enrollment.ShouldNotBeNull();
        enrollment.StudentName.ShouldBe("Alice");
        enrollment.Assignments.ShouldBe(["HW1"]);
    }

    [Fact]
    public async Task aggregate_by_tags_through_a_query_session_is_null_without_events()
    {
        ConfigureStore();

        await using IQuerySession query = theStore.QuerySession();
        var enrollment = await query.Events.AggregateByTagsAsync<StudentCourseEnrollment>(
            new EventTagQuery().Or<StudentId>(new StudentId(Guid.NewGuid())));

        enrollment.ShouldBeNull();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task events_exist_through_a_query_session(DcbStorageMode mode)
    {
        ConfigureStore(mode);
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        await AppendOnTwoStreams(studentId, courseId, "Alice", "HW1");

        await using IQuerySession query = theStore.QuerySession();
        (await query.Events.EventsExistAsync(new EventTagQuery().Or<StudentId>(studentId))).ShouldBeTrue();
        (await query.Events.EventsExistAsync(new EventTagQuery().Or<StudentId>(new StudentId(Guid.NewGuid()))))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task tag_queries_through_a_query_session_are_isolated_by_tenant()
    {
        ConfigureStore(tenancy: TenancyStyle.Conjoined);
        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());
        await AppendOnTwoStreams(studentId, courseId, "Alice", "HW-A", TenantA);
        await AppendOnTwoStreams(studentId, courseId, "Bob", "HW-B", TenantB);

        var tagQuery = new EventTagQuery().Or<StudentId>(studentId);

        await using IQuerySession queryA = theStore.QuerySession(TenantA);
        var eventsA = await queryA.Events.QueryByTagsAsync(tagQuery);
        eventsA.Count.ShouldBe(2);
        eventsA[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");

        await using IQuerySession queryB = theStore.QuerySession(TenantB);
        var enrollmentB = await queryB.Events.AggregateByTagsAsync<StudentCourseEnrollment>(tagQuery);
        enrollmentB.ShouldNotBeNull();
        enrollmentB.StudentName.ShouldBe("Bob");
        enrollmentB.Assignments.ShouldBe(["HW-B"]);

        await using IQuerySession queryC = theStore.QuerySession("tenant-c");
        (await queryC.Events.EventsExistAsync(tagQuery)).ShouldBeFalse();
    }
}
