#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record ExplorationCourseId(Guid Value);
public record ExplorationStudentId(Guid Value);
public record ExplorationSeriesId(Guid Value);
public record ExplorationUsername(string Value);

public record ExplorationSubscribed(Guid CourseId, Guid StudentId);
public record ExplorationInvoiceCreated(int Number);
public record ExplorationInvoiceViewed(string By);
public record ExplorationUsernameClaimed(string Username);
public record ExplorationAccountClosed(string Username);
public record ExplorationUsernameLookedUp(string Username);

[BoundaryAggregate]
public class ExplorationSubscriptions
{
    public List<(Guid CourseId, Guid StudentId)> Subscriptions { get; set; } = new();

    public void Apply(ExplorationSubscribed e) => Subscriptions.Add((e.CourseId, e.StudentId));
}

[BoundaryAggregate]
public class ExplorationInvoiceSeries
{
    public int Highest { get; set; }

    public void Apply(ExplorationInvoiceCreated e) => Highest = Math.Max(Highest, e.Number);
}

[BoundaryAggregate]
public class ExplorationUsernameState
{
    public bool Taken { get; set; }

    public void Apply(ExplorationUsernameClaimed _) => Taken = true;
    public void Apply(ExplorationAccountClosed _) => Taken = false;
}

// #5887 exploratory run: behaviours adapted from other DCB implementations' test suites (the
// dcb.events examples, AxonFramework's StorageEngineTestSuite, Happenstance's contract rules,
// wwwision/dcb-eventstore(-doctrine), kraken-tech/dcb-event-store, UmaDB / pyeventsourcing),
// restated against Marten's API. Each test names the case it adapts. Shapes Marten's EventTagQuery
// cannot express -- several tags AND'd inside one condition, a condition with no tag at all, an
// explicit `after` position -- are left out rather than approximated.
[Collection("OneOffs")]
public class dcb_append_condition_exploration: OneOffConfigurationsContext
{
    private DocumentStore Use(DcbConflictScope scope, DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        return StoreOptions(opts =>
        {
            opts.Events.AddEventType<ExplorationSubscribed>();
            opts.Events.AddEventType<ExplorationInvoiceCreated>();
            opts.Events.AddEventType<ExplorationInvoiceViewed>();
            opts.Events.AddEventType<ExplorationUsernameClaimed>();
            opts.Events.AddEventType<ExplorationAccountClosed>();
            opts.Events.AddEventType<ExplorationUsernameLookedUp>();

            opts.Events.RegisterTagType<ExplorationCourseId>("x_course");
            opts.Events.RegisterTagType<ExplorationStudentId>("x_student");
            opts.Events.RegisterTagType<ExplorationSeriesId>("x_series");
            opts.Events.RegisterTagType<ExplorationUsername>("x_username");

            opts.Events.DcbStorageMode = mode;
            opts.Events.DcbConflictScope = scope;

            // Deadlocks must fail, not commit on a retry -- see Bug_5887_boundary_check_does_not_bump_what_it_reads
            opts.ConfigureWritePolly(_ => { });
        });
    }

    private static IEvent Build<T>(IDocumentSession session, T data, params object[] tags) where T : notnull
    {
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        return e;
    }

    // Happenstance E2E-06 / kraken: a batch is not checked against its own events. A boundary that
    // appends several events its own query matches commits all of them.
    [Fact]
    public async Task a_batch_matching_its_own_query_commits_every_event()
    {
        var store = Use(DcbConflictScope.TagValueAndEventType);
        var series = new ExplorationSeriesId(Guid.NewGuid());
        var query = new EventTagQuery().Or(series).AndEventsOfType<ExplorationInvoiceCreated>();

        await using (var session = store.LightweightSession())
        {
            var boundary = await session.Events.FetchForWritingByTags<ExplorationInvoiceSeries>(query);
            boundary.AppendMany(
                Build(session, new ExplorationInvoiceCreated(1), series),
                Build(session, new ExplorationInvoiceCreated(2), series),
                Build(session, new ExplorationInvoiceCreated(3), series));
            await session.SaveChangesAsync();
        }

        await using var query2 = store.LightweightSession();
        (await query2.Events.QueryByTagsAsync(query)).Count.ShouldBe(3);
    }

    // Happenstance / kraken / sliceworkz: a rejected append writes nothing -- not its events, not its
    // tags, and not the version bumps its events would have made.
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_rejected_append_writes_nothing(DcbStorageMode mode)
    {
        var store = Use(DcbConflictScope.TagValueAndEventType, mode);
        var series = new ExplorationSeriesId(Guid.NewGuid());
        var query = new EventTagQuery().Or(series).AndEventsOfType<ExplorationInvoiceCreated>();

        await using var loser = store.LightweightSession();
        var boundary = await loser.Events.FetchForWritingByTags<ExplorationInvoiceSeries>(query);

        await using (var winner = store.LightweightSession())
        {
            var b = await winner.Events.FetchForWritingByTags<ExplorationInvoiceSeries>(query);
            b.AppendOne(Build(winner, new ExplorationInvoiceCreated(1), series));
            await winner.SaveChangesAsync();
        }

        var versionsBefore = await VersionsAsync(series.Value.ToString());

        boundary.AppendOne(Build(loser, new ExplorationInvoiceCreated(1), series));
        boundary.AppendOne(Build(loser, new ExplorationInvoiceViewed("loser"), series));
        await Should.ThrowAsync<DcbConcurrencyException>(() => loser.SaveChangesAsync());

        await using var check = store.LightweightSession();
        (await check.Events.QueryByTagsAsync(new EventTagQuery().Or(series))).Count.ShouldBe(1);
        (await VersionsAsync(series.Value.ToString())).ShouldBe(versionsBefore);
    }

    // Happenstance k_disjoint_boundaries_never_conflict / Axon concurrentTransactionsForNonOverlappingTags:
    // concurrent decisions over disjoint boundaries never conflict, and never deadlock.
    [Theory]
    [InlineData(DcbConflictScope.TagValue)]
    [InlineData(DcbConflictScope.TagValueAndEventType)]
    public async Task disjoint_boundaries_in_parallel_never_conflict(DcbConflictScope scope)
    {
        var store = Use(scope);
        const int boundaries = 24;
        var failures = new ConcurrentBag<Exception>();

        for (var round = 0; round < 10; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, boundaries).Select(_ => Task.Run(async () =>
            {
                try
                {
                    var series = new ExplorationSeriesId(Guid.NewGuid());
                    await using var session = store.LightweightSession();
                    var boundary = await session.Events.FetchForWritingByTags<ExplorationInvoiceSeries>(
                        new EventTagQuery().Or(series).AndEventsOfType<ExplorationInvoiceCreated>());
                    boundary.AppendOne(Build(session, new ExplorationInvoiceCreated(1), series));
                    await session.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            })));
        }

        failures.ShouldBeEmpty();
    }

    // wwwision/dcb-eventstore-doctrine conditional_append_fails_if_an_event_was_in_flight_during_the_decision:
    // writer A has appended a matching event (taking the LOWER sequence) but not committed. B appends
    // and commits a matching event above it. The decision reads -- seeing B, not A -- and A then
    // commits. The decision did not see A's event, so its append must fail. A store that checks
    // "anything matching above the highest position I read" misses this; Marten's versions do not.
    [Fact]
    public async Task an_event_in_flight_below_what_the_decision_read_still_conflicts()
    {
        var store = Use(DcbConflictScope.TagValue);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var course = new ExplorationCourseId(Guid.NewGuid());
        var student = new ExplorationStudentId(Guid.NewGuid());

        // The decision reads both tags; A and B each carry a different one, so B's bump does not
        // wait on A's row lock
        var query = new EventTagQuery().Or(course).Or(student);

        // Writer A runs in its own async flow, so its ambient TransactionScope (and Npgsql's
        // auto-enlistment) never reaches the other sessions in this test
        var appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = Task.Run(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            await using (var session = store.LightweightSession(SessionOptions.ForCurrentTransaction()))
            {
                session.Events.Append(Guid.NewGuid(),
                    Build(session, new ExplorationSubscribed(course.Value, Guid.NewGuid()), course));
                await session.SaveChangesAsync();
            }

            appended.SetResult();
            await commit.Task;
            scope.Complete();
        });

        await appended.Task;

        await using (var committed = store.LightweightSession())
        {
            committed.Events.Append(Guid.NewGuid(), Build(committed, new ExplorationSubscribed(Guid.NewGuid(), student.Value), student));
            await committed.SaveChangesAsync();
        }

        await using var decision = store.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<ExplorationSubscriptions>(query);
        boundary.Events.Count.ShouldBe(1); // B only -- A is not committed yet

        commit.SetResult();
        await inFlight;

        boundary.AppendOne(Build(decision, new ExplorationSubscribed(course.Value, student.Value), course, student));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    // Happenstance condition_with_an_unheld_tag_does_not_reject: an event of a selected type carrying
    // a DIFFERENT value of the tag is not a match.
    [Fact]
    public async Task an_event_of_a_selected_type_with_another_tag_value_does_not_conflict()
    {
        var store = Use(DcbConflictScope.TagValueAndEventType);
        var mine = new ExplorationUsername("alice-" + Guid.NewGuid());
        var other = new ExplorationUsername("bob-" + Guid.NewGuid());

        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<ExplorationUsernameState>(
            new EventTagQuery().Or(mine).AndEventsOfType<ExplorationUsernameClaimed, ExplorationAccountClosed>());

        await using (var elsewhere = store.LightweightSession())
        {
            elsewhere.Events.Append(Guid.NewGuid(), Build(elsewhere, new ExplorationUsernameClaimed(other.Value), other));
            await elsewhere.SaveChangesAsync();
        }

        boundary.AppendOne(Build(session, new ExplorationUsernameClaimed(mine.Value), mine));
        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    // dcb.events "unique username" example, raced. One tag value, several selected types, and a
    // stream of events of an UNSELECTED type (look-ups) on the same tag value landing throughout.
    // Never more than one claim, and nothing but a DcbConcurrencyException is ever thrown.
    [Theory]
    [InlineData(DcbConflictScope.TagValue, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.HStore)]
    public async Task a_raced_username_is_claimed_exactly_once(DcbConflictScope scope, DcbStorageMode mode)
    {
        var store = Use(scope, mode);
        const int claimants = 12;

        for (var round = 0; round < 15; round++)
        {
            var username = new ExplorationUsername("user-" + Guid.NewGuid());
            var query = new EventTagQuery().Or(username)
                .AndEventsOfType<ExplorationUsernameClaimed, ExplorationAccountClosed>();
            var gate = new AsyncGate(claimants + 1);
            var unexpected = new ConcurrentBag<Exception>();

            var lookUps = Task.Run(async () =>
            {
                await gate.ArriveAndWaitAsync();
                for (var i = 0; i < 5; i++)
                {
                    await using var session = store.LightweightSession();
                    session.Events.Append(Guid.NewGuid(), Build(session, new ExplorationUsernameLookedUp(username.Value), username));
                    await session.SaveChangesAsync();
                }
            });

            var claims = Enumerable.Range(0, claimants).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await using var session = store.LightweightSession();
                    var boundary = await session.Events.FetchForWritingByTags<ExplorationUsernameState>(query);
                    await gate.ArriveAndWaitAsync();
                    if (boundary.Aggregate is { Taken: true }) return;

                    boundary.AppendOne(Build(session, new ExplorationUsernameClaimed(username.Value), username));
                    await session.SaveChangesAsync();
                }
                catch (DcbConcurrencyException)
                {
                }
                catch (Exception e)
                {
                    unexpected.Add(e);
                }
            }));

            await Task.WhenAll(claims.Append(lookUps));

            unexpected.ShouldBeEmpty();

            await using var check = store.LightweightSession();
            var claimed = (await check.Events.QueryByTagsAsync(
                new EventTagQuery().Or(username).AndEventsOfType<ExplorationUsernameClaimed>())).Count;

            // Never two claims. Under TagValue a look-up landing between a claimant's read and its
            // save is a conflict too, so a round can end with NO claim when every claimant was
            // knocked out by look-ups -- the false conflict #5887 is about. Scoped by event type,
            // look-ups are invisible to claimants and exactly one claim always wins.
            if (scope == DcbConflictScope.TagValueAndEventType)
            {
                claimed.ShouldBe(1);
            }
            else
            {
                claimed.ShouldBeLessThanOrEqualTo(1);
            }
        }
    }

    // dcb.events "invoice number" example as a randomized invariant check: concurrent allocators
    // each read the series, take the next number and retry on conflict, while events of another
    // type keep landing on the same series. The committed numbers must be exactly 1..N -- no gap,
    // no duplicate -- whatever the scope.
    [Theory]
    [InlineData(DcbConflictScope.TagValue, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.HStore)]
    public async Task concurrently_allocated_invoice_numbers_are_gapless_and_unique(DcbConflictScope scope,
        DcbStorageMode mode)
    {
        var store = Use(scope, mode);
        var series = new ExplorationSeriesId(Guid.NewGuid());
        var query = new EventTagQuery().Or(series).AndEventsOfType<ExplorationInvoiceCreated>();
        const int allocators = 10;
        const int invoicesEach = 5;
        var unexpected = new ConcurrentBag<Exception>();
        using var stop = new CancellationTokenSource();

        var noise = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await using var session = store.LightweightSession();
                session.Events.Append(Guid.NewGuid(), Build(session, new ExplorationInvoiceViewed("auditor"), series));
                await session.SaveChangesAsync();

                // Unthrottled, this starves every allocator under TagValue: each view invalidates
                // every open read of the series, and 500 retries apiece are not enough to get one
                // allocation through. Scoped by event type, views never conflict at all.
                await Task.Delay(5);
            }
        });

        await Task.WhenAll(Enumerable.Range(0, allocators).Select(_ => Task.Run(async () =>
        {
            for (var invoice = 0; invoice < invoicesEach; invoice++)
            {
                for (var attempt = 0;; attempt++)
                {
                    try
                    {
                        await using var session = store.LightweightSession();
                        var boundary = await session.Events.FetchForWritingByTags<ExplorationInvoiceSeries>(query);
                        var next = (boundary.Aggregate?.Highest ?? 0) + 1;
                        boundary.AppendOne(Build(session, new ExplorationInvoiceCreated(next), series));
                        await session.SaveChangesAsync();
                        break;
                    }
                    catch (DcbConcurrencyException) when (attempt < 500)
                    {
                    }
                    catch (Exception e)
                    {
                        unexpected.Add(e);
                        return;
                    }
                }
            }
        })));

        stop.Cancel();
        await noise;

        unexpected.ShouldBeEmpty();

        await using var check = store.LightweightSession();
        var numbers = (await check.Events.QueryByTagsAsync(query))
            .Select(e => ((ExplorationInvoiceCreated)e.Data).Number).OrderBy(x => x).ToArray();
        numbers.ShouldBe(Enumerable.Range(1, allocators * invoicesEach).ToArray());
    }

    // dcb.events "course subscriptions" example as a randomized invariant check: a two-condition
    // query (the course's subscriptions OR the student's), a course capacity and a per-student
    // limit, raced across a small grid so decisions overlap on both tags. Neither limit may ever
    // be exceeded.
    [Theory]
    [InlineData(DcbConflictScope.TagValue, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.TagTables)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.HStore)]
    public async Task raced_course_subscriptions_respect_both_limits(DcbConflictScope scope, DcbStorageMode mode)
    {
        var store = Use(scope, mode);
        const int courseCapacity = 3;
        const int studentLimit = 2;
        var courses = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var students = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var unexpected = new ConcurrentBag<Exception>();

        await Task.WhenAll(Enumerable.Range(0, 48).Select(i => Task.Run(async () =>
        {
            var course = courses[Random.Shared.Next(courses.Length)];
            var student = students[i % students.Length];
            var query = new EventTagQuery()
                .Or(new ExplorationCourseId(course)).AndEventsOfType<ExplorationSubscribed>()
                .Or(new ExplorationStudentId(student)).AndEventsOfType<ExplorationSubscribed>();

            try
            {
                await using var session = store.LightweightSession();
                var boundary = await session.Events.FetchForWritingByTags<ExplorationSubscriptions>(query);
                var subscriptions = boundary.Aggregate?.Subscriptions ?? [];

                if (subscriptions.Count(x => x.CourseId == course) >= courseCapacity) return;
                if (subscriptions.Count(x => x.StudentId == student) >= studentLimit) return;
                if (subscriptions.Contains((course, student))) return;

                boundary.AppendOne(Build(session, new ExplorationSubscribed(course, student),
                    new ExplorationCourseId(course), new ExplorationStudentId(student)));
                await session.SaveChangesAsync();
            }
            catch (DcbConcurrencyException)
            {
            }
            catch (Exception e)
            {
                unexpected.Add(e);
            }
        })));

        unexpected.ShouldBeEmpty();

        await using var check = store.LightweightSession();
        var all = (await check.Events.QueryByTagsAsync(courses.Aggregate(new EventTagQuery(),
                (q, c) => q.Or(new ExplorationCourseId(c)))))
            .Select(e => (ExplorationSubscribed)e.Data).ToList();

        all.GroupBy(x => x.CourseId).ShouldAllBe(g => g.Count() <= courseCapacity);
        all.GroupBy(x => x.StudentId).ShouldAllBe(g => g.Count() <= studentLimit);
        all.GroupBy(x => (x.CourseId, x.StudentId)).ShouldAllBe(g => g.Count() == 1);
        all.ShouldNotBeEmpty();
    }

    private async Task<string> VersionsAsync(string tagValue)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select coalesce(string_agg(tag_table || '=' || version, ',' order by tag_table), '') from {SchemaName}.mt_dcb_tag_version where tag_value = @value";
        cmd.Parameters.AddWithValue("value", tagValue);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

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
