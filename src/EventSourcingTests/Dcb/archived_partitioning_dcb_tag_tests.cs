using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public class archived_partitioning_dcb_tag_tests : OneOffConfigurationsContext
{
    public record struct ArchStudentId(Guid Value);
    public record struct ArchCourseId(Guid Value);
    public record ArchStudentEnrolled(string Name, string Course);

    [Fact]
    public async Task can_create_schema_with_archived_partitioning_and_tags()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<ArchStudentId>("arch_student");
            opts.Events.RegisterTagType<ArchCourseId>("arch_course");
            opts.Events.AddEventType<ArchStudentEnrolled>();
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Idempotent
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task can_append_events_with_tags_and_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<ArchStudentId>("arch_student");
            opts.Events.RegisterTagType<ArchCourseId>("arch_course");
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var studentId = new ArchStudentId(Guid.NewGuid());
        var courseId = new ArchCourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        var enrolled = session.Events.BuildEvent(new ArchStudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        session.Events.StartStream(streamId, enrolled);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task can_query_events_exist_with_tags_and_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<ArchStudentId>("arch_student");
            opts.Events.RegisterTagType<ArchCourseId>("arch_course");
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var studentId = new ArchStudentId(Guid.NewGuid());
        var courseId = new ArchCourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var enrolled = session.Events.BuildEvent(new ArchStudentEnrolled("Bob", "Science"));
            enrolled.WithTag(studentId, courseId);
            session.Events.StartStream(streamId, enrolled);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            var exists = await query.Events.EventsExistAsync(
                new EventTagQuery().Or<ArchStudentId>(studentId));
            exists.ShouldBeTrue();
        }
    }
}
