#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record SeatingClassId(Guid Value);
public record WaitlistClassId(Guid Value);

public record ClassSeatTaken(Guid MemberId);
public record ClassWaitlistJoined(Guid MemberId);

[BoundaryAggregate]
public class ClassSeating
{
    public int SeatsTaken { get; set; }

    public void Apply(ClassSeatTaken _) => SeatsTaken++;
}

// #5887: the DCB append condition fails only when an event MATCHING THE QUERY was appended after
// the read. Marten's boundary save used to bump the mt_dcb_tag_version row of every tag it read,
// so two boundaries reading the same tag value conflicted even when neither one appended an event
// carrying that tag. The check now locks and compares the rows it read and leaves the versions
// alone; only the producer-side bump of the appended events' own tags moves a version.
[Collection("OneOffs")]
public class Bug_5887_boundary_check_does_not_bump_what_it_reads: OneOffConfigurationsContext
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<ClassSeatTaken>();
            opts.Events.AddEventType<ClassWaitlistJoined>();

            opts.Events.RegisterTagType<SeatingClassId>("seating_class");
            opts.Events.RegisterTagType<WaitlistClassId>("waitlist_class");

            // No write retries: a retried deadlock (40P01) commits after a one-second
            // deadlock_timeout and passes, so with retries on these tests cannot tell a save that
            // waited its turn from one that deadlocked.
            opts.ConfigureWritePolly(_ => { });
        });
    }

    private static EventTagQuery SeatingQuery(Guid classId)
        => new EventTagQuery().Or(new SeatingClassId(classId));

    // The appended event carries only the waitlist tag, which the seating query does not read
    private static IEvent JoinWaitlist(IDocumentSession session, Guid classId)
    {
        var joined = session.Events.BuildEvent(new ClassWaitlistJoined(Guid.NewGuid()));
        joined.WithTag(new WaitlistClassId(classId));
        return joined;
    }

    private static IEvent TakeSeat(IDocumentSession session, Guid classId)
    {
        var taken = session.Events.BuildEvent(new ClassSeatTaken(Guid.NewGuid()));
        taken.WithTag(new SeatingClassId(classId));
        return taken;
    }

    private async Task SeedSeatAsync(Guid classId)
    {
        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        boundary.AppendOne(TakeSeat(session, classId));
        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task two_boundaries_whose_appends_carry_no_tag_the_other_reads_both_commit(bool rowExists)
    {
        ConfigureStore();
        var classId = Guid.NewGuid();
        if (rowExists) await SeedSeatAsync(classId);

        await using var a = theStore.LightweightSession();
        await using var b = theStore.LightweightSession();
        var boundaryA = await a.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        var boundaryB = await b.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));

        boundaryA.AppendOne(JoinWaitlist(a, classId));
        boundaryB.AppendOne(JoinWaitlist(b, classId));

        await a.SaveChangesAsync();
        await Should.NotThrowAsync(() => b.SaveChangesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task the_check_leaves_the_version_of_a_row_it_only_read_alone(bool rowExists)
    {
        ConfigureStore();
        var classId = Guid.NewGuid();
        if (rowExists) await SeedSeatAsync(classId);

        var before = await TagVersionAsync("seating_class", classId) ?? 0;

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        boundary.AppendOne(JoinWaitlist(session, classId));
        await session.SaveChangesAsync();

        ((await TagVersionAsync("seating_class", classId)) ?? 0).ShouldBe(before);

        // ...and the tag the appended event does carry still moves
        (await TagVersionAsync("waitlist_class", classId)).ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task an_append_matching_the_query_still_conflicts(bool rowExists)
    {
        ConfigureStore();
        var classId = Guid.NewGuid();
        if (rowExists) await SeedSeatAsync(classId);

        await using var a = theStore.LightweightSession();
        await using var b = theStore.LightweightSession();
        var boundaryA = await a.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
        var boundaryB = await b.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));

        boundaryA.AppendOne(TakeSeat(a, classId));
        boundaryB.AppendOne(JoinWaitlist(b, classId));

        await a.SaveChangesAsync();
        await Should.ThrowAsync<DcbConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task a_plain_append_matching_the_query_still_conflicts()
    {
        ConfigureStore();
        var classId = Guid.NewGuid();

        await using var a = theStore.LightweightSession();
        var boundaryA = await a.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));

        await using (var other = theStore.LightweightSession())
        {
            other.Events.Append(Guid.NewGuid(), TakeSeat(other, classId));
            await other.SaveChangesAsync();
        }

        boundaryA.AppendOne(JoinWaitlist(a, classId));
        await Should.ThrowAsync<DcbConcurrencyException>(() => a.SaveChangesAsync());
    }

    // The reporter's load shape: many concurrent decisions over one boundary, none of which appends
    // anything the others read. They serialize on the row lock, and every one of them commits.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task concurrent_boundaries_whose_appends_the_others_do_not_read_all_commit(bool rowExists)
    {
        ConfigureStore();
        const int racers = 16;

        for (var round = 0; round < 20; round++)
        {
            var classId = Guid.NewGuid();
            if (rowExists) await SeedSeatAsync(classId);

            var fetched = new AsyncGate(racers);
            var failures = new ConcurrentBag<Exception>();

            await Task.WhenAll(Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await using var session = theStore.LightweightSession();
                    var boundary =
                        await session.Events.FetchForWritingByTags<ClassSeating>(SeatingQuery(classId));
                    await fetched.ArriveAndWaitAsync();
                    boundary.AppendOne(JoinWaitlist(session, classId));
                    await session.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            })));

            failures.ShouldBeEmpty();
        }
    }

    // Two decisions that read each other's tags: A reads the seating tag and appends a waitlist
    // event, B reads the waitlist tag and appends a seat. Each one's append matches the other's
    // query, so exactly one of them commits -- and the loser must lose with a DcbConcurrencyException,
    // never a deadlock. The check's row locks and the producer bumps' row locks are taken in one
    // sorted pass, so two such saves cannot each hold the row the other is waiting on.
    [Fact]
    public async Task crossed_boundaries_lose_with_a_dcb_conflict_not_a_deadlock()
    {
        ConfigureStore();

        for (var round = 0; round < 50; round++)
        {
            var classId = Guid.NewGuid();
            var fetched = new AsyncGate(2);
            var failures = new ConcurrentBag<Exception>();

            async Task Decide(EventTagQuery query, Func<IDocumentSession, IEvent> append)
            {
                try
                {
                    await using var session = theStore.LightweightSession();
                    var boundary = await session.Events.FetchForWritingByTags<ClassSeating>(query);
                    await fetched.ArriveAndWaitAsync();
                    boundary.AppendOne(append(session));
                    await session.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }

            await Task.WhenAll(
                Task.Run(() => Decide(SeatingQuery(classId), s => JoinWaitlist(s, classId))),
                Task.Run(() => Decide(new EventTagQuery().Or(new WaitlistClassId(classId)), s => TakeSeat(s, classId))));

            failures.Count.ShouldBe(1);
            foreach (var failure in failures)
            {
                failure.ShouldBeOfType<DcbConcurrencyException>();
            }
        }
    }

    private async Task<long?> TagVersionAsync(string tagTable, Guid value)
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select version from {SchemaName}.mt_dcb_tag_version where tag_table = @table and tag_value = @value";
        cmd.Parameters.AddWithValue("table", tagTable);
        cmd.Parameters.AddWithValue("value", value.ToString());

        var raw = await cmd.ExecuteScalarAsync();
        return raw == null || raw == DBNull.Value ? null : Convert.ToInt64(raw);
    }

    // Every racer captures its version before any of them saves, without parking thread-pool threads
    private class AsyncGate(int count)
    {
        private int _arrived;
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == count) _open.TrySetResult();
            return _open.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
