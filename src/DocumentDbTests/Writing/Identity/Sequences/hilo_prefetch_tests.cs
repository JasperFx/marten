using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class PrefetchedIntDoc
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class PrefetchedLongDoc
{
    public long Id { get; set; }
}

/// <summary>
///     #5862: ids are still assigned synchronously in Store(), but the next HiLo block is reserved ahead of
///     time with an asynchronous round trip. The synchronous fetch is only the fallback for a block that is
///     needed before its prefetch has landed.
/// </summary>
// #5935: blocks thread-pool threads on purpose, so it must not run beside other tests
[Collection(HiloThreadPoolCollection.Name)]
public class hilo_prefetch_tests: OneOffConfigurationsContext
{
    private IMartenDatabase database()
    {
        var database = theStore.Tenancy.Default.Database;

        // Reading Sequences applies mt_hilo and mt_get_next_hi
        database.Sequences.ShouldNotBeNull();
        return database;
    }

    // Every test reserves blocks under its own entity name, so exact hi values are deterministic
    private static string uniqueEntity() => "prefetch_" + Guid.NewGuid().ToString("N");

    private HiloSequence realSequence(int maxLo)
    {
        return new HiloSequence(database(), theStore.Options, uniqueEntity(), new HiloSettings { MaxLo = maxLo });
    }

    [Fact]
    public async Task the_prefetched_block_replaces_an_exhausted_one_without_a_synchronous_fetch()
    {
        var sequence = realSequence(5);

        sequence.NextLong().ShouldBe(1);
        sequence.SynchronousFetchCount.ShouldBe(1); // the very first block has nothing to prefetch from

        await sequence.WaitForPrefetchAsync();
        sequence.PrefetchFailureCount.ShouldBe(0, sequence.LastPrefetchFailure?.ToString());

        var ids = Enumerable.Range(0, 9).Select(_ => sequence.NextLong()).ToArray();

        // Crossing the block boundary at 6 used the prefetched block, so nothing was skipped
        ids.ShouldBe(new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        sequence.SynchronousFetchCount.ShouldBe(1);
        sequence.PrefetchedBlockCount.ShouldBe(1);
        sequence.PrefetchFailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task falls_back_to_a_synchronous_fetch_when_the_prefetch_has_not_landed()
    {
        // A block size of 1 exhausts the block on every id, and the prefetch cannot reach the database
        // until the gate opens. Every id until then has to come from the synchronous fallback
        var sequence = new GatedSequence(database(), theStore.Options, uniqueEntity(), 1, reserveBeforeGate: false);

        var ids = Enumerable.Range(0, 5).Select(_ => sequence.NextLong()).ToList();

        ids.ShouldBe(new long[] { 1, 2, 3, 4, 5 });
        sequence.SynchronousFetchCount.ShouldBe(5);
        sequence.PrefetchedBlockCount.ShouldBe(0);

        sequence.OpenGate();
        await sequence.WaitForPrefetchAsync();

        // Only one prefetch is ever in flight, however many fallbacks ran meanwhile
        sequence.PrefetchesStarted.ShouldBe(1);

        // The prefetch reserved its hi after the five synchronous ones, so it is the next block
        sequence.NextLong().ShouldBe(6);
        sequence.SynchronousFetchCount.ShouldBe(5);
        sequence.PrefetchedBlockCount.ShouldBe(1);
    }

    [Fact]
    public async Task a_prefetch_overtaken_by_the_fallback_is_discarded_rather_than_issued_out_of_order()
    {
        // The prefetch reserves hi 1 straight away but is held back from delivering it, so the synchronous
        // fallback for the second id reserves hi 2
        var sequence = new GatedSequence(database(), theStore.Options, uniqueEntity(), 1, reserveBeforeGate: true);

        sequence.NextLong().ShouldBe(1);
        await sequence.PrefetchReserved;

        sequence.NextLong().ShouldBe(3);

        sequence.OpenGate();
        await sequence.WaitForPrefetchAsync();

        // hi 1 arrived after hi 2 was installed, so handing it out now would go backwards: it is a gap
        sequence.NextLong().ShouldBe(4);
        sequence.DiscardedPrefetchCount.ShouldBe(1);
        sequence.PrefetchedBlockCount.ShouldBe(0);
        sequence.SynchronousFetchCount.ShouldBe(3);
    }

    [Fact]
    public async Task the_synchronous_path_never_waits_on_a_prefetch_in_flight()
    {
        // #5969: the synchronous path used to wait up to five seconds for a prefetch still in flight (#5938).
        // That blocked a thread-pool thread on asynchronous work that needed a thread-pool thread to finish,
        // which is how a burst of synchronous Store() calls starved the pool. It now fetches a block of its own
        var sequence = new GatedSequence(database(), theStore.Options, uniqueEntity(), 1, reserveBeforeGate: false);

        sequence.NextLong().ShouldBe(1);

        // The prefetch of the second block cannot reach the database until the gate opens
        var stopwatch = Stopwatch.StartNew();
        sequence.NextLong().ShouldBe(2);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        sequence.SynchronousFetchCount.ShouldBe(2);
        sequence.PrefetchedBlockCount.ShouldBe(0);

        sequence.OpenGate();
        await sequence.WaitForPrefetchAsync();

        // The prefetch reserved its hi after the fallback's, so it is simply the next block
        sequence.NextLong().ShouldBe(3);
        sequence.SynchronousFetchCount.ShouldBe(2);
        sequence.PrefetchedBlockCount.ShouldBe(1);
        sequence.DiscardedPrefetchCount.ShouldBe(0);
    }

    [Fact]
    public async Task racing_a_prefetch_for_the_hilo_row_costs_the_synchronous_path_no_attempts()
    {
        // #5938, deterministically: the prefetch has updated the mt_hilo row but not committed yet, as a
        // real prefetch has while its round trip is in flight. The synchronous fetch for the next block now
        // starts at once (#5969) and blocks on the row lock. Under the old compare-and-set mt_get_next_hi it
        // then found the value it read gone once the prefetch committed and got -1 back, and with a budget of
        // one attempt it threw HiloSequenceAdvanceToNextHiAttemptsExceededException. The atomic increment
        // makes it wait for the row and take the block after the prefetch's instead
        var sequence = new RowHoldingSequence(database(), theStore.Options, uniqueEntity());

        sequence.NextLong().ShouldBe(1);
        await sequence.PrefetchHoldsTheRow;

        var next = Task.Run(() => sequence.NextLong());

        // Let the prefetch commit only once the synchronous fetch is waiting behind its row lock
        var sawTheRace = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!next.IsCompleted && DateTime.UtcNow < deadline)
        {
            if (sequence.SynchronousFetchesStarted > 1 && await aFetchIsWaitingOnTheHiloRow())
            {
                sawTheRace = true;
                break;
            }

            await Task.Delay(10);
        }

        sequence.Commit();

        sawTheRace.ShouldBeTrue("the synchronous fetch never queued behind the prefetch's row lock");

        // The prefetch reserved hi 1 and the synchronous fetch hi 2
        (await next).ShouldBe(3);
        sequence.SynchronousFetchCount.ShouldBe(2);
        sequence.PrefetchedBlockCount.ShouldBe(0);

        // hi 1 landed behind hi 2, so it is a gap rather than ids issued out of order
        await sequence.WaitForPrefetchAsync();
        sequence.NextLong().ShouldBe(4);
        sequence.DiscardedPrefetchCount.ShouldBe(1);
        sequence.PrefetchedBlockCount.ShouldBe(0);
    }

    private async Task<bool> aFetchIsWaitingOnTheHiloRow()
    {
        await using var conn = theStore.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from pg_stat_activity where datname = current_database() " +
                          "and wait_event_type = 'Lock' and query like '%mt_get_next_hi%'";
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    [Fact]
    public async Task the_async_path_waits_for_the_prefetch_instead_of_fetching_again()
    {
        var sequence = new GatedSequence(database(), theStore.Options, uniqueEntity(), 1, reserveBeforeGate: false);

        sequence.NextLong().ShouldBe(1);

        var next = sequence.NextLongAsync().AsTask();
        await Task.Delay(100);
        next.IsCompleted.ShouldBeFalse();

        sequence.OpenGate();
        (await next).ShouldBe(2);

        sequence.SynchronousFetchCount.ShouldBe(1);
        sequence.PrefetchedBlockCount.ShouldBe(1);
    }

    [Fact]
    public async Task a_failed_prefetch_falls_back_and_never_poisons_the_sequence()
    {
        var unobserved = new ConcurrentBag<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            foreach (var inner in e.Exception.Flatten().InnerExceptions)
            {
                if (inner.Message == FaultingSequence.Message) unobserved.Add(inner);
            }
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var sequence = new FaultingSequence(database(), theStore.Options, uniqueEntity(), 2);

            var ids = new List<long>();
            for (var i = 0; i < 10; i++)
            {
                ids.Add(sequence.NextLong());
                await sequence.WaitForPrefetchAsync();
            }

            ids.ShouldBe(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            sequence.PrefetchFailureCount.ShouldBe(5);
            sequence.LastPrefetchFailure.ShouldBeOfType<InvalidOperationException>()
                .Message.ShouldBe(FaultingSequence.Message);
            sequence.PrefetchedBlockCount.ShouldBe(0);
            sequence.SynchronousFetchCount.ShouldBe(5);

            // Once the database is reachable again the prefetch takes over: the failed prefetch is
            // swapped out for a synchronous fetch, which starts a prefetch that now succeeds
            sequence.Fail = false;
            ids.Add(sequence.NextLong());
            ids.Add(sequence.NextLong());
            await sequence.WaitForPrefetchAsync();
            ids.Add(sequence.NextLong());

            ids.Skip(10).ShouldBe(new long[] { 11, 12, 13 });
            sequence.PrefetchedBlockCount.ShouldBe(1);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            unobserved.ShouldBeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task resetting_the_floor_discards_a_block_reserved_below_it()
    {
        var sequence = realSequence(5);

        sequence.NextLong().ShouldBe(1);
        await sequence.WaitForPrefetchAsync(); // hi 1 is now reserved and waiting
        sequence.PrefetchFailureCount.ShouldBe(0, sequence.LastPrefetchFailure?.ToString());

        await sequence.SetFloor(1000);
        sequence.DiscardedPrefetchCount.ShouldBe(1);

        await sequence.WaitForPrefetchAsync();
        var ids = Enumerable.Range(0, 12).Select(_ => sequence.NextLong()).ToArray();

        ids.ShouldAllBe(id => id > 1000);
        ids.ShouldBe(ids.Distinct().OrderBy(x => x));
    }

    [Fact]
    public async Task concurrent_sync_and_async_callers_never_get_the_same_id()
    {
        // Three ids per block forces a rollover, and a prefetch, every few calls
        var sequence = realSequence(3);

        const int callers = 16;
        const int idsPerCaller = 300;

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, callers).Select(caller => Task.Run(async () =>
        {
            await start.Task;
            var ids = new List<long>(idsPerCaller);
            for (var i = 0; i < idsPerCaller; i++)
            {
                // Half the callers mix the asynchronous path in, which shares the same gate and prefetch
                ids.Add(caller % 2 == 0 || i % 3 != 0 ? sequence.NextLong() : await sequence.NextLongAsync());
            }

            return ids;
        })).ToArray();

        start.SetResult();
        var all = (await Task.WhenAll(tasks)).SelectMany(x => x).ToArray();

        all.Length.ShouldBe(callers * idsPerCaller);
        all.Distinct().Count().ShouldBe(all.Length);

        // Each caller saw its own ids in increasing order: no block was ever handed out behind a later one
        foreach (var ids in tasks.Select(x => x.Result))
        {
            ids.ShouldBe(ids.OrderBy(x => x));
        }
    }

    [Fact]
    public async Task concurrent_sessions_storing_documents_get_unique_ids()
    {
        StoreOptions(opts =>
        {
            opts.Advanced.HiloSequenceDefaults.MaxLo = 3;
            opts.Schema.For<PrefetchedIntDoc>();
            opts.Schema.For<PrefetchedLongDoc>();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(PrefetchedIntDoc));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(PrefetchedLongDoc));

        const int writers = 12;
        const int sessionsPerWriter = 10;
        const int docsPerSession = 20;

        var assigned = new ConcurrentBag<int>();
        var assignedLongs = new ConcurrentBag<long>();

        await Task.WhenAll(Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
        {
            for (var s = 0; s < sessionsPerWriter; s++)
            {
                await using var session = theStore.LightweightSession();
                for (var d = 0; d < docsPerSession; d++)
                {
                    var doc = new PrefetchedIntDoc { Name = $"{writer}-{s}-{d}" };
                    session.Store(doc);

                    // The id is set the moment the document is stored
                    doc.Id.ShouldBeGreaterThan(0);
                    assigned.Add(doc.Id);

                    var other = new PrefetchedLongDoc();
                    session.Store(other);
                    other.Id.ShouldBeGreaterThan(0);
                    assignedLongs.Add(other.Id);
                }

                await session.SaveChangesAsync();
            }
        })));

        const int expected = writers * sessionsPerWriter * docsPerSession;

        assigned.Count.ShouldBe(expected);
        assigned.Distinct().Count().ShouldBe(expected);
        assignedLongs.Distinct().Count().ShouldBe(expected);

        // A duplicate id would have been an upsert over another writer's document
        await using var query = theStore.QuerySession();
        (await query.Query<PrefetchedIntDoc>().CountAsync()).ShouldBe(expected);
        (await query.Query<PrefetchedLongDoc>().CountAsync()).ShouldBe(expected);

        var sequence = theStore.Tenancy.Default.Database.Sequences.SequenceFor(typeof(PrefetchedIntDoc))
            .ShouldBeOfType<HiloSequence>();
        await sequence.WaitForPrefetchAsync();
    }

    /// <summary>
    ///     A real sequence whose prefetch is held back until <see cref="OpenGate" />. The synchronous
    ///     fallback is untouched. With <c>reserveBeforeGate</c> the prefetch reserves its hi in the database
    ///     first and only then waits, which lets a test make the fallback overtake it.
    /// </summary>
    private sealed class GatedSequence: HiloSequence
    {
        private readonly bool _reserveBeforeGate;
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _reserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _prefetchesStarted;

        public GatedSequence(IMartenDatabase database, StoreOptions options, string entityName, int maxLo,
            bool reserveBeforeGate)
            : base(database, options, entityName, new HiloSettings { MaxLo = maxLo })
        {
            _reserveBeforeGate = reserveBeforeGate;
        }

        public int PrefetchesStarted => Volatile.Read(ref _prefetchesStarted);

        public Task PrefetchReserved => _reserved.Task;

        public void OpenGate() => _gate.TrySetResult();

        internal override async Task<long> FetchNextHiAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _prefetchesStarted);

            if (_reserveBeforeGate)
            {
                var hi = await base.FetchNextHiAsync(ct);
                _reserved.TrySetResult();
                await _gate.Task;
                return hi;
            }

            await _gate.Task;
            return await base.FetchNextHiAsync(ct);
        }
    }

    /// <summary>
    ///     A real sequence with a budget of one attempt per fetch, whose prefetch calls mt_get_next_hi in
    ///     a transaction it keeps open, holding the updated mt_hilo row, until <see cref="Commit" />.
    /// </summary>
    private sealed class RowHoldingSequence: HiloSequence
    {
        private readonly IMartenDatabase _database;
        private readonly string _schema;
        private readonly TaskCompletionSource _holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _commit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _synchronousFetchesStarted;

        public RowHoldingSequence(IMartenDatabase database, StoreOptions options, string entityName)
            : base(database, options, entityName, new HiloSettings { MaxLo = 1, MaxAdvanceToNextHiAttempts = 1 })
        {
            _database = database;
            _schema = options.DatabaseSchemaName;
        }

        public Task PrefetchHoldsTheRow => _holding.Task;

        public int SynchronousFetchesStarted => Volatile.Read(ref _synchronousFetchesStarted);

        public void Commit() => _commit.TrySetResult();

        internal override long FetchNextHiSync()
        {
            Interlocked.Increment(ref _synchronousFetchesStarted);
            return base.FetchNextHiSync();
        }

        internal override async Task<long> FetchNextHiAsync(CancellationToken ct)
        {
            await using var conn = _database.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"select {_schema}.mt_get_next_hi(@entity)";
            cmd.Parameters.AddWithValue("entity", EntityName);
            var hi = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));

            _holding.TrySetResult();
            await _commit.Task;

            await tx.CommitAsync(ct);
            return hi;
        }
    }

    /// <summary>
    ///     A real sequence whose asynchronous fetch fails while <see cref="Fail" /> is set.
    /// </summary>
    private sealed class FaultingSequence: HiloSequence
    {
        public const string Message = "#5862: the prefetch could not reach the database";

        public FaultingSequence(IMartenDatabase database, StoreOptions options, string entityName, int maxLo)
            : base(database, options, entityName, new HiloSettings { MaxLo = maxLo })
        {
        }

        public volatile bool Fail = true;

        internal override async Task<long> FetchNextHiAsync(CancellationToken ct)
        {
            await Task.Yield();
            if (Fail) throw new InvalidOperationException(Message);

            return await base.FetchNextHiAsync(ct);
        }
    }
}
