#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Dcb;

/// <summary>
/// #4617 section 3f deferred — Bug_4591 redux under
/// <c>UseTenantPartitionedEvents</c>. The original #4591 fix replaced the
/// predicate-read DCB consistency check with an UPDATE-on-side-table
/// (<see cref="Marten.Events.Dcb.DcbTagVersionTable"/> +
/// <see cref="Marten.Events.Dcb.DcbTagVersionAssertion"/>) that acquires a
/// row lock + does an optimistic version check in one statement, serializing
/// concurrent appends through that single row.
///
/// <para>
/// This test pins that the side-table serialization still works correctly
/// when <c>UseTenantPartitionedEvents</c> is also on — the per-tenant event
/// partitioning does NOT bypass the DCB side-table lock. Eight true racers
/// sharing a CourseId tag must collapse to exactly ONE committed append; the
/// other seven must observe <see cref="DcbConcurrencyException"/>.
/// </para>
///
/// <para>
/// Run against both <see cref="DcbStorageMode.HStore"/> and
/// <see cref="DcbStorageMode.TagTables"/> just like the original Bug_4591 —
/// the side-table fix has to hold for both physical-storage shapes.
/// </para>
/// </summary>
public class dcb_concurrency_replay_under_partitioning
{
    private readonly ITestOutputHelper _output;

    public dcb_concurrency_replay_under_partitioning(ITestOutputHelper output)
    {
        _output = output;
    }

    private static async Task<DocumentStore> BuildStoreAsync(DcbStorageMode storageMode, string schema)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.DcbStorageMode = storageMode;
            opts.Events.AddEventType<DcbReplayStudentEnrolled>();
            opts.Events.AddEventType<DcbReplayAssignmentSubmitted>();

            opts.Events.RegisterTagType<DcbReplayStudentId>("dcbr_student")
                .ForAggregate<DcbReplayStudentCourseEnrollment>();
            opts.Events.RegisterTagType<DcbReplayCourseId>("dcbr_course")
                .ForAggregate<DcbReplayStudentCourseEnrollment>();

            opts.Projections.LiveStreamAggregation<DcbReplayStudentCourseEnrollment>();
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        return store;
    }

    [Theory]
    [InlineData(DcbStorageMode.HStore)]
    [InlineData(DcbStorageMode.TagTables)]
    public async Task truly_concurrent_appends_sharing_a_tag_serialize_under_partitioning(DcbStorageMode storageMode)
    {
        var schema = $"tp_dcbc_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        using var store = await BuildStoreAsync(storageMode, schema);

        const int Racers = 8;
        const string TenantId = "acme";

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantId);

        // The shared CourseId — every racer carries it. This is the DCB
        // boundary tag the side-table serializes on.
        var courseId = new DcbReplayCourseId(Guid.NewGuid());

        // Seed event under a different stream so lastSeenSequence > 0 going
        // into the race.
        var seedStudentId = new DcbReplayStudentId(Guid.NewGuid());
        await using (var seedSession = store.LightweightSession(TenantId))
        {
            var enrolled = seedSession.Events.BuildEvent(new DcbReplayStudentEnrolled("Seed", "Math"));
            enrolled.WithTag(seedStudentId, courseId);
            seedSession.Events.Append(seedStudentId.Value, enrolled);
            await seedSession.SaveChangesAsync();
        }

        // Synchronize the fetch → save handoff so every racer captures the
        // same DCB tag version, then they all race into SaveChangesAsync at
        // once. The side-table UPDATE on (tag_table, tag_value, tenant_id)
        // serializes them.
        var allFetched = new TaskCompletionSource[Racers];
        for (var i = 0; i < Racers; i++) allFetched[i] = new TaskCompletionSource();
        var startSaves = new TaskCompletionSource();

        var query = new EventTagQuery().Or<DcbReplayCourseId>(courseId);

        var racerTasks = Enumerable.Range(0, Racers).Select(i => Task.Run(async () =>
        {
            var racerStudentId = new DcbReplayStudentId(Guid.NewGuid());

            await using var session = store.LightweightSession(TenantId);
            var boundary = await session.Events.FetchForWritingByTags<DcbReplayStudentCourseEnrollment>(query);

            allFetched[i].SetResult();
            await startSaves.Task;

            var append = session.Events.BuildEvent(new DcbReplayAssignmentSubmitted($"HW-{i}", 80 + i));
            append.WithTag(racerStudentId, courseId);
            boundary.AppendOne(append);

            try
            {
                await session.SaveChangesAsync();
                return (Index: i, Committed: true, Exception: (Exception?)null);
            }
            catch (DcbConcurrencyException ex)
            {
                return (Index: i, Committed: false, Exception: (Exception?)ex);
            }
        })).ToArray();

        await Task.WhenAll(allFetched.Select(t => t.Task));
        startSaves.SetResult();

        var results = await Task.WhenAll(racerTasks);

        var committed = results.Count(r => r.Committed);
        var throws = results.Count(r => r.Exception is DcbConcurrencyException);
        var otherErrors = results.Where(r => r.Exception is not null and not DcbConcurrencyException).ToList();

        _output.WriteLine($"DCB storage mode under partitioning: {storageMode}");
        _output.WriteLine($"Truly-concurrent racers: {Racers}");
        _output.WriteLine($"  Committed:                {committed}");
        _output.WriteLine($"  DcbConcurrencyException:  {throws}");
        if (otherErrors.Count > 0)
        {
            _output.WriteLine($"  Other exceptions:         {otherErrors.Count}");
            foreach (var er in otherErrors)
            {
                _output.WriteLine($"    Racer {er.Index}: {er.Exception}");
            }
        }

        // Exactly one racer commits; everyone else observes the DCB violation.
        // No other exception type — if partitioning had introduced a deadlock
        // or a different concurrency surface, this would surface here.
        committed.ShouldBe(1,
            $"Expected exactly one truly-concurrent append to commit under partitioning; " +
            $"observed {committed} commits + {throws} DcbConcurrencyException throws (mode = {storageMode}).");
        throws.ShouldBe(Racers - 1);
        otherErrors.ShouldBeEmpty();
    }
}

// Local types so the test stays self-contained — avoids any cross-file
// collision with other DCB tests' StudentId / CourseId / event names.
public record DcbReplayStudentId(Guid Value);
public record DcbReplayCourseId(Guid Value);

public record DcbReplayStudentEnrolled(string Name, string Course);
public record DcbReplayAssignmentSubmitted(string AssignmentName, int Score);

public class DcbReplayStudentCourseEnrollment
{
    public Guid Id { get; set; }
    public int AssignmentCount { get; set; }

    public void Apply(DcbReplayStudentEnrolled _) { }
    public void Apply(DcbReplayAssignmentSubmitted _) => AssignmentCount++;
}
