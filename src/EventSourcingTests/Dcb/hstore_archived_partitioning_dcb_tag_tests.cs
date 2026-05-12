#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// Parallel of <see cref="archived_partitioning_dcb_tag_tests"/> for
/// <see cref="DcbStorageMode.HStore"/>. With archived-stream partitioning,
/// <c>mt_events</c> is partitioned by <c>is_archived</c>. The hstore <c>tags</c>
/// column lives on the partitioned parent and the GIN index is automatically
/// propagated to every partition by Postgres' declarative partitioning, so the
/// scenario is structurally equivalent — these tests confirm that schema
/// creation, append, and EventsExistAsync all work end-to-end.
/// </summary>
public class hstore_archived_partitioning_dcb_tag_tests: OneOffConfigurationsContext
{
    public record struct HsArchStudentId(Guid Value);
    public record struct HsArchCourseId(Guid Value);
    public record HsArchStudentEnrolled(string Name, string Course);

    [Fact]
    public async Task can_create_schema_with_archived_partitioning_and_hstore_tags()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<HsArchStudentId>("hs_arch_student");
            opts.Events.RegisterTagType<HsArchCourseId>("hs_arch_course");
            opts.Events.AddEventType<HsArchStudentEnrolled>();
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Idempotent
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task can_append_events_with_hstore_tags_and_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<HsArchStudentId>("hs_arch_student");
            opts.Events.RegisterTagType<HsArchCourseId>("hs_arch_course");
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var studentId = new HsArchStudentId(Guid.NewGuid());
        var courseId = new HsArchCourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        var enrolled = session.Events.BuildEvent(new HsArchStudentEnrolled("Alice", "Math"));
        enrolled.WithTag(studentId, courseId);
        session.Events.StartStream(streamId, enrolled);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task can_query_events_exist_with_hstore_tags_and_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<HsArchStudentId>("hs_arch_student");
            opts.Events.RegisterTagType<HsArchCourseId>("hs_arch_course");
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var studentId = new HsArchStudentId(Guid.NewGuid());
        var courseId = new HsArchCourseId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var enrolled = session.Events.BuildEvent(new HsArchStudentEnrolled("Bob", "Science"));
            enrolled.WithTag(studentId, courseId);
            session.Events.StartStream(streamId, enrolled);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            var exists = await query.Events.EventsExistAsync(
                new EventTagQuery().Or<HsArchStudentId>(studentId));
            exists.ShouldBeTrue();
        }
    }
}
