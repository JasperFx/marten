#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     #6035: a tombstone deleting a <c>mt_dcb_tag_version</c> row while another save adds the tag. A save
///     holds that row from its bump until it commits, so the tombstone takes the rows in a statement of its
///     own, and only then, from a fresh snapshot, asks whether another stream carries the tag. These hold one
///     save open at its commit point and watch what the other does. Polecat pins the same facts
///     (polecat#890).
/// </summary>
public class Bug_6035_tombstone_erasure_concurrency: OneOffConfigurationsContext
{
    private void configure(DcbStorageMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.RegisterTagType<Bug6035TagId>("race_course");
            opts.Events.RegisterTagType<Bug6035OtherId>("race_student");
            opts.Events.DcbStorageMode = mode;
        });
    }

    /// <summary>Stops a save between the last of its SQL and its commit.</summary>
    private sealed class HoldAtCommit: ITransactionParticipant
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        public async Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            _reached.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
    }

    private static IEvent tagged(IDocumentSession session, params object[] tags)
    {
        var e = session.Events.BuildEvent(new Bug6035Happened("one"));
        e.WithTag(tags);
        return e;
    }

    private async Task<Guid> appendAsync(params object[] tags)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, tagged(session, tags));
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private Task<long> versionRowsAsync(Guid tagValue)
        => scalarAsync(
            $"select count(*) from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0 and tag_value = '{tagValue}'");

    /// <summary>The other save holds what this one needs, so it can't have finished.</summary>
    private static async Task shouldStillBeWaitingAsync(Task blocked)
    {
        await Task.Delay(750);
        blocked.IsCompleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_tombstone_behind_an_append_in_flight_sees_the_append_and_keeps_the_row(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);

        var hold = new HoldAtCommit();
        await using var appending = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)appending).AddTransactionParticipant(hold);
        appending.Events.Append(Guid.NewGuid(), tagged(appending, course));
        var append = appending.SaveChangesAsync();
        await hold.Reached;

        await using var tombstoning = theStore.LightweightSession();
        tombstoning.Events.TombstoneStream(doomed);
        var tombstone = tombstoning.SaveChangesAsync();

        await shouldStillBeWaitingAsync(tombstone);

        hold.Release();
        await append;
        await tombstone;

        (await versionRowsAsync(course.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_append_behind_a_tombstone_creates_the_row_again(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);

        var hold = new HoldAtCommit();
        await using var tombstoning = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)tombstoning).AddTransactionParticipant(hold);
        tombstoning.Events.TombstoneStream(doomed);
        var tombstone = tombstoning.SaveChangesAsync();
        await hold.Reached;

        await using var appending = theStore.LightweightSession();
        appending.Events.Append(Guid.NewGuid(), tagged(appending, course));
        var append = appending.SaveChangesAsync();

        await shouldStillBeWaitingAsync(append);

        hold.Release();
        await tombstone;
        await append;

        (await versionRowsAsync(course.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_behind_a_tombstone_loses(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);

        await using var bounded = theStore.LightweightSession();
        var boundary = await bounded.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(course));

        var hold = new HoldAtCommit();
        await using var tombstoning = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)tombstoning).AddTransactionParticipant(hold);
        tombstoning.Events.TombstoneStream(doomed);
        var tombstone = tombstoning.SaveChangesAsync();
        await hold.Reached;

        boundary.AppendOne(tagged(bounded, course));
        var save = bounded.SaveChangesAsync();

        await shouldStillBeWaitingAsync(save);

        hold.Release();
        await tombstone;
        await Should.ThrowAsync<DcbConcurrencyException>(() => save);

        (await versionRowsAsync(course.Value)).ShouldBe(0);
    }

    /// <summary>
    ///     Every stream ends up tombstoned, so whatever order they landed in, nothing may be left. A row left
    ///     behind is a tombstone that gave way to a stream it then outlived.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task appends_and_tombstones_racing_over_shared_tags_leave_the_table_right(DcbStorageMode mode)
    {
        configure(mode);
        var courses = Enumerable.Range(0, 3).Select(_ => new Bug6035TagId(Guid.NewGuid())).ToArray();
        var students = Enumerable.Range(0, 3).Select(_ => new Bug6035OtherId(Guid.NewGuid())).ToArray();

        var workers = Enumerable.Range(0, 6).Select(worker => Task.Run(async () =>
        {
            var random = new Random(worker);

            for (var round = 0; round < 15; round++)
            {
                var stream = await appendAsync(courses[random.Next(3)], students[random.Next(3)]);

                await using var session = theStore.LightweightSession();
                session.Events.TombstoneStream(stream);
                await session.SaveChangesAsync();
            }
        })).ToArray();

        await Task.WhenAll(workers);

        (await scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version")).ShouldBe(0);

        foreach (var course in courses) await appendAsync(course);
        (await scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0"))
            .ShouldBe(courses.Length);
    }
}
