#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using JasperFx;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug6081CourseId(Guid Value);

public record Bug6081Enrolled(string Student);

/// <summary>
///     #6081: a store built with Marten 9's defaults (no archived stream partitioning) and DCB tag tables has to
///     migrate to Marten 10's defaults, where mt_events is partitioned by is_archived and each tag table's foreign
///     key is on (seq_id, is_archived). The tagged events have to survive the migration.
/// </summary>
public class Bug_6081_v9_tag_tables_upgrade_to_10_defaults: OneOffConfigurationsContext
{
    private static void configure(StoreOptions opts, TenancyStyle tenancy)
    {
        opts.Events.TenancyStyle = tenancy;
        opts.Events.AddEventType<Bug6081Enrolled>();
        opts.Events.RegisterTagType<Bug6081CourseId>("bug6081_course");
    }

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public async Task tag_tables_migrate_to_archived_partitioning_and_keep_their_events(TenancyStyle tenancy)
    {
        // Marten 9's shape, as an existing database has it
        var v9 = StoreOptions(opts =>
        {
            opts.RestoreV9Defaults();
            configure(opts, tenancy);
        });
        v9.Options.Events.UseArchivedStreamPartitioning.ShouldBeFalse();

        await v9.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var course = new Bug6081CourseId(Guid.NewGuid());
        var other = new Bug6081CourseId(Guid.NewGuid());
        var streams = new System.Collections.Generic.Dictionary<string, Guid>();

        await using (var session = v9.LightweightSession("tenant1"))
        {
            foreach (var (student, tag) in new[] { ("Alice", course), ("Bob", course), ("Carol", other), ("Erin", course) })
            {
                var e = session.Events.BuildEvent(new Bug6081Enrolled(student));
                e.WithTag(tag);
                streams[student] = session.Events.StartStream(e).Id;
            }

            await session.SaveChangesAsync();
        }

        // An archived event under Marten 9 is a row of mt_events with is_archived = true, and its tag row says nothing
        await using (var session = v9.LightweightSession("tenant1"))
        {
            session.Events.ArchiveStream(streams["Erin"]);
            await session.SaveChangesAsync();
        }

        v9.Dispose();

        // Marten 10's defaults, from a store that shares no state with the first
        var v10 = SeparateStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            configure(opts, tenancy);
        });
        v10.Options.Events.UseArchivedStreamPartitioning.ShouldBeTrue();

        await v10.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await v10.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        (await studentsTaggedWith(v10, course)).ShouldBe(["Alice", "Bob"]);
        (await studentsTaggedWith(v10, other)).ShouldBe(["Carol"]);

        // Each tag row took its event's is_archived, so the archived event's tag row is in the archived partition
        (await countAsync($"select count(*) from {SchemaName}.mt_event_tag_bug6081_course_archived")).ShouldBe(1);
        (await countAsync($"select count(*) from {SchemaName}.mt_event_tag_bug6081_course_default")).ShouldBe(3);

        // Archiving, un-archiving and appending all still work on the migrated rows
        await using (var session = v10.LightweightSession("tenant1"))
        {
            session.Events.ArchiveStream(streams["Bob"]);
            session.Events.UnArchiveStream(streams["Erin"]);

            var e = session.Events.BuildEvent(new Bug6081Enrolled("Dave"));
            e.WithTag(course);
            session.Events.StartStream(Guid.NewGuid(), e);
            await session.SaveChangesAsync();
        }

        (await studentsTaggedWith(v10, course)).ShouldBe(["Alice", "Dave", "Erin"]);
        (await countAsync($"select count(*) from {SchemaName}.mt_event_tag_bug6081_course_archived")).ShouldBe(1);
    }

    private static async Task<string[]> studentsTaggedWith(IDocumentStore store, Bug6081CourseId tag)
    {
        await using var query = store.QuerySession("tenant1");
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or<Bug6081CourseId>(tag));
        return events.Select(x => x.Data).OfType<Bug6081Enrolled>().Select(x => x.Student).OrderBy(x => x).ToArray();
    }

    private static async Task<long> countAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
