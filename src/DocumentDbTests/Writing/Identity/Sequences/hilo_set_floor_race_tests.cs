using System;
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

/// <summary>
///     SetFloor is serialized with id issuance: once <c>SetFloor(n)</c> has returned, no id at or below
///     <c>n</c> is issued, even by an advance that reserved its block under the old floor and was still in
///     flight when the floor moved.
/// </summary>
// Holds a thread on purpose, so it runs with the other HiLo concurrency tests and never beside anything else
[Collection(HiloThreadPoolCollection.Name)]
public class hilo_set_floor_race_tests: OneOffConfigurationsContext
{
    private const long Floor = 1000;

    private IMartenDatabase database()
    {
        var database = theStore.Tenancy.Default.Database;

        // Reading Sequences applies mt_hilo and mt_get_next_hi
        database.Sequences.ShouldNotBeNull();
        return database;
    }

    // Every test reserves blocks under its own entity name, so exact hi values are deterministic
    private HeldFetchSequence sequence() =>
        new(database(), theStore.Options, "set_floor_" + Guid.NewGuid().ToString("N"), 2);

    [Fact]
    public async Task a_synchronous_advance_in_flight_cannot_install_a_block_from_below_a_new_floor()
    {
        var sequence = this.sequence();

        // No prefetch, so the next exhausted block takes the synchronous fetch
        sequence.FailAsyncFetches = true;
        sequence.NextLong().ShouldBe(1);
        sequence.NextLong().ShouldBe(2);
        await sequence.WaitForPrefetchAsync();

        // The synchronous fetch reserves hi 1 under the old floor, then is held before installing it
        var hold = sequence.HoldNextSynchronousFetch();
        var next = Task.Factory.StartNew(() => sequence.NextLong(), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        (await hold.Reserved.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(1);

        sequence.FailAsyncFetches = false;
        await sequence.SetFloor(Floor);

        hold.Release();
        var id = await next.WaitAsync(TimeSpan.FromSeconds(10));

        // Before the fix this was 3: hi 1 landed on top of the block SetFloor installed
        id.ShouldBeGreaterThan(Floor);
        await assertEveryLaterIdIsAboveTheFloor(sequence, id);
    }

    [Fact]
    public async Task an_asynchronous_advance_in_flight_cannot_install_a_block_from_below_a_new_floor()
    {
        var sequence = this.sequence();

        // The prefetch fails, so the asynchronous advance fetches a block of its own
        sequence.FailAsyncFetches = true;
        sequence.NextLong().ShouldBe(1);
        sequence.NextLong().ShouldBe(2);
        await sequence.WaitForPrefetchAsync();

        // The asynchronous fetch reserves hi 1 under the old floor, then is held before installing it
        var hold = sequence.HoldNextAsynchronousFetch();
        sequence.FailAsyncFetches = false;
        var next = sequence.NextLongAsync().AsTask();

        (await hold.Reserved.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(1);

        await sequence.SetFloor(Floor);

        hold.Release();
        var id = await next.WaitAsync(TimeSpan.FromSeconds(10));

        // Before the fix this was 3: hi 1 landed on top of the block SetFloor installed
        id.ShouldBeGreaterThan(Floor);
        await assertEveryLaterIdIsAboveTheFloor(sequence, id);
    }

    private static async Task assertEveryLaterIdIsAboveTheFloor(HiloSequence sequence, long first)
    {
        await sequence.WaitForPrefetchAsync();

        var ids = new[] { first }
            .Concat(Enumerable.Range(0, 6).Select(_ => sequence.NextLong()))
            .ToArray();

        ids.ShouldAllBe(x => x > Floor);
        ids.ShouldBe(ids.Distinct().OrderBy(x => x));
    }

    /// <summary>
    ///     Reserves its hi in the database for real, then holds a single chosen fetch back before returning
    ///     it, so a test can move the floor between the reservation and the install.
    /// </summary>
    private sealed class HeldFetchSequence: HiloSequence
    {
        private FetchHold? _synchronous;
        private FetchHold? _asynchronous;

        public HeldFetchSequence(IMartenDatabase database, StoreOptions options, string entityName, int maxLo)
            : base(database, options, entityName, new HiloSettings { MaxLo = maxLo })
        {
        }

        public volatile bool FailAsyncFetches;

        public FetchHold HoldNextSynchronousFetch() => _synchronous = new FetchHold();

        public FetchHold HoldNextAsynchronousFetch() => _asynchronous = new FetchHold();

        internal override long FetchNextHiSync()
        {
            var hi = base.FetchNextHiSync();
            Interlocked.Exchange(ref _synchronous, null)?.Block(hi);
            return hi;
        }

        internal override async Task<long> FetchNextHiAsync(CancellationToken ct)
        {
            var hold = Interlocked.Exchange(ref _asynchronous, null);
            if (hold == null && FailAsyncFetches)
            {
                await Task.Yield();
                throw new InvalidOperationException("The test turned asynchronous fetches off");
            }

            var hi = await base.FetchNextHiAsync(ct);
            if (hold != null) await hold.BlockAsync(hi);
            return hi;
        }
    }

    private sealed class FetchHold
    {
        private readonly TaskCompletionSource<long> _reserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<long> Reserved => _reserved.Task;

        public void Release() => _release.TrySetResult();

        public void Block(long hi)
        {
            _reserved.TrySetResult(hi);
            _release.Task.Wait(TimeSpan.FromSeconds(30));
        }

        public async Task BlockAsync(long hi)
        {
            _reserved.TrySetResult(hi);
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
