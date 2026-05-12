#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// Parallel of <see cref="conjoined_tenancy_dcb_tag_tests"/> for
/// <see cref="DcbStorageMode.HStore"/>. Validates that the per-tenant filters added
/// to the HStore branches in <c>EventStore.Dcb.BuildTagQuerySql</c>,
/// <c>EventsExistByTagsHandler</c>, <c>FetchForWritingByTagsHandler</c>,
/// <c>AssertDcbConsistency</c>, and <c>SetEventTagsHstoreOperation</c> correctly
/// isolate DCB query, exists, aggregate, fetch-for-writing, and consistency-check
/// results between tenants.
/// </summary>
[Collection("OneOffs")]
public class hstore_conjoined_tenancy_dcb_tag_tests: OneOffConfigurationsContext
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private void ConfigureConjoinedStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.AddEventType<AssignmentSubmitted>();
            opts.Events.AddEventType<StudentDropped>();
            opts.Events.AddEventType<StudentGraded>();

            opts.Events.RegisterTagType<StudentId>("student")
                .ForAggregate<StudentCourseEnrollment>();
            opts.Events.RegisterTagType<CourseId>("course")
                .ForAggregate<StudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });
    }

    [Fact]
    public async Task can_create_schema_with_conjoined_tenancy_and_hstore_tags()
    {
        ConfigureConjoinedStore();

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task schema_is_idempotent_with_conjoined_tenancy_and_hstore_tags()
    {
        ConfigureConjoinedStore();

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task tag_queries_are_isolated_by_tenant()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        var enrolledA = sessionA.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolledA.WithTag(studentId, courseId);
        sessionA.Events.Append(streamA, enrolledA);
        await sessionA.SaveChangesAsync();

        await using var sessionB = theStore.LightweightSession(TenantB);
        var streamB = Guid.NewGuid();
        var enrolledB = sessionB.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        enrolledB.WithTag(studentId, courseId);
        sessionB.Events.Append(streamB, enrolledB);
        await sessionB.SaveChangesAsync();

        await using var queryA = theStore.LightweightSession(TenantA);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var eventsA = await queryA.Events.QueryByTagsAsync(query);
        eventsA.Count.ShouldBe(1);
        eventsA[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Alice");

        await using var queryB = theStore.LightweightSession(TenantB);
        var eventsB = await queryB.Events.QueryByTagsAsync(query);
        eventsB.Count.ShouldBe(1);
        eventsB[0].Data.ShouldBeOfType<StudentEnrolled>().StudentName.ShouldBe("Bob");
    }

    [Fact]
    public async Task events_exist_is_isolated_by_tenant()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA = theStore.LightweightSession(TenantA);
        var enrolled = sessionA.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        sessionA.Events.Append(Guid.NewGuid(), enrolled);
        await sessionA.SaveChangesAsync();

        var query = new EventTagQuery().Or<StudentId>(studentId);

        await using var queryA = theStore.LightweightSession(TenantA);
        (await queryA.Events.EventsExistAsync(query)).ShouldBeTrue();

        await using var queryB = theStore.LightweightSession(TenantB);
        (await queryB.Events.EventsExistAsync(query)).ShouldBeFalse();
    }

    [Fact]
    public async Task aggregate_by_tags_is_isolated_by_tenant()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        var enrolledA = sessionA.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolledA.WithTag(studentId, courseId);
        var hwA = sessionA.Events.BuildEvent(new AssignmentSubmitted("HW-A", 95));
        hwA.WithTag(studentId, courseId);
        sessionA.Events.Append(streamA, enrolledA, hwA);
        await sessionA.SaveChangesAsync();

        await using var sessionB = theStore.LightweightSession(TenantB);
        var streamB = Guid.NewGuid();
        var enrolledB = sessionB.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        enrolledB.WithTag(studentId, courseId);
        var hwB = sessionB.Events.BuildEvent(new AssignmentSubmitted("HW-B", 80));
        hwB.WithTag(studentId, courseId);
        sessionB.Events.Append(streamB, enrolledB, hwB);
        await sessionB.SaveChangesAsync();

        var query = new EventTagQuery()
            .Or<StudentId>(studentId)
            .Or<CourseId>(courseId);

        await using var queryA = theStore.LightweightSession(TenantA);
        var aggA = await queryA.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
        aggA.ShouldNotBeNull();
        aggA.StudentName.ShouldBe("Alice");
        aggA.Assignments.ShouldContain("HW-A");
        aggA.Assignments.ShouldNotContain("HW-B");

        await using var queryB = theStore.LightweightSession(TenantB);
        var aggB = await queryB.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
        aggB.ShouldNotBeNull();
        aggB.StudentName.ShouldBe("Bob");
        aggB.Assignments.ShouldContain("HW-B");
        aggB.Assignments.ShouldNotContain("HW-A");
    }

    [Fact]
    public async Task fetch_for_writing_by_tags_is_isolated_by_tenant()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        var enrolled = sessionA.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        sessionA.Events.Append(streamA, enrolled);
        await sessionA.SaveChangesAsync();

        await using var sessionB = theStore.LightweightSession(TenantB);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await sessionB.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);
        boundary.Aggregate.ShouldBeNull();
        boundary.Events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task dcb_concurrency_check_is_isolated_by_tenant()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA1 = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        var enrolled = sessionA1.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        sessionA1.Events.Append(streamA, enrolled);
        await sessionA1.SaveChangesAsync();

        await using var sessionA2 = theStore.LightweightSession(TenantA);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await sessionA2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using var sessionB = theStore.LightweightSession(TenantB);
        var streamB = Guid.NewGuid();
        var enrolledB = sessionB.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
        enrolledB.WithTag(studentId, courseId);
        sessionB.Events.Append(streamB, enrolledB);
        await sessionB.SaveChangesAsync();

        var hw = sessionA2.Events.BuildEvent(new AssignmentSubmitted("HW1", 90));
        hw.WithTag(studentId, courseId);
        boundary.AppendOne(hw);

        await sessionA2.SaveChangesAsync(); // must succeed; tenant B's event is invisible
    }

    [Fact]
    public async Task dcb_concurrency_detects_same_tenant_conflict()
    {
        ConfigureConjoinedStore();

        var studentId = new StudentId(Guid.NewGuid());
        var courseId = new CourseId(Guid.NewGuid());

        await using var sessionA1 = theStore.LightweightSession(TenantA);
        var streamA = Guid.NewGuid();
        var enrolled = sessionA1.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        sessionA1.Events.Append(streamA, enrolled);
        await sessionA1.SaveChangesAsync();

        await using var session1 = theStore.LightweightSession(TenantA);
        var query = new EventTagQuery().Or<StudentId>(studentId);
        var boundary = await session1.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

        await using var session2 = theStore.LightweightSession(TenantA);
        var conflicting = session2.Events.BuildEvent(new AssignmentSubmitted("HW-conflict", 50));
        conflicting.WithTag(studentId, courseId);
        session2.Events.Append(streamA, conflicting);
        await session2.SaveChangesAsync();

        var hw = session1.Events.BuildEvent(new AssignmentSubmitted("HW1", 90));
        hw.WithTag(studentId, courseId);
        boundary.AppendOne(hw);

        await Should.ThrowAsync<DcbConcurrencyException>(() => session1.SaveChangesAsync());
    }

    [Fact]
    public async Task can_create_schema_with_conjoined_tenancy_archived_partitioning_and_hstore_tags()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.RegisterTagType<CourseId>("course");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
