using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core.Sequences;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

/// <summary>
///     #5998 on the 9.x line: once <c>SetFloor(n)</c> has returned, no id at or below <c>n</c> is issued, even by a
///     synchronous advance that reserved its block under the old floor and was still in flight when the floor moved.
///     Weasel 9.51.0 (weasel#849/#853) serializes <c>HiloSequenceBase.TrySetCurrentHi</c> with <c>NextLong</c>, so
///     <c>SetFloor</c> now waits for the advance in flight and installs its block after it.
/// </summary>
/// <remarks>
///     Before 9.51.0 the held advance installed its old block on top of the one <c>SetFloor</c> had installed, and
///     the next id was 4.
/// </remarks>
// Holds a thread on purpose, so it runs with the other HiLo concurrency tests and never beside anything else
[Collection(HiloThreadPoolCollection.Name)]
public class Bug_5998_set_floor_race: OneOffConfigurationsContext
{
    private const long Floor = 1000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task a_synchronous_advance_in_flight_cannot_install_a_block_from_below_a_new_floor()
    {
        var database = theStore.Tenancy.Default.Database;

        // Reading Sequences applies mt_hilo and mt_get_next_hi
        database.Sequences.ShouldNotBeNull();

        // Its own entity name, so the hi values reserved below are exact
        var sequence = new HeldAdvanceSequence(database, theStore.Options, "bug_5998_" + Guid.NewGuid().ToString("N"));

        // Use up hi 0, so the next NextLong has to advance
        sequence.NextLong().ShouldBe(1);
        sequence.NextLong().ShouldBe(2);

        // That advance reserves hi 1 in the database under the old floor, then is held before installing it,
        // inside NextLong's lock
        sequence.HoldNextSynchronousAdvance();
        var inFlight = Task.Factory.StartNew(() => sequence.NextLong(), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        (await sequence.Reserved.Task.WaitAsync(Timeout)).ShouldBe(1);

        // The floor moves while the advance is held
        sequence.WatchInstallsFromSetFloor();
        var setFloor = Task.Factory.StartNew(() => sequence.SetFloor(Floor), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        // Release the advance only once SetFloor has either returned (Weasel before 9.51.0) or is blocked trying
        // to install its first block behind the held advance (9.51.0). Either way the outcome below no longer
        // depends on timing
        var state = await sequence.WaitForSetFloorToFinishOrBlockAsync(setFloor, Timeout);

        sequence.Release();
        var issuedByTheAdvanceInFlight = await inFlight.WaitAsync(Timeout);
        await setFloor.WaitAsync(Timeout);

        // The call that was already part way through its advance when the floor moved keeps its old block: it
        // started first
        issuedByTheAdvanceInFlight.ShouldBe(3);

        // Every id issued after SetFloor has returned is above the floor. These were 4 and 1005 before the fix:
        // hi 1 had landed on top of the block SetFloor installed
        var after = Enumerable.Range(0, 6).Select(_ => sequence.NextLong()).ToArray();
        after.ShouldAllBe(id => id > Floor,
            $"ids issued after SetFloor({Floor}) returned, with SetFloor {state} while the advance was held: {string.Join(", ", after)}");
        after.ShouldBe(after.Distinct().OrderBy(x => x));
    }

    /// <summary>
    ///     Reserves every hi from the database for real, through <c>mt_get_next_hi</c>, and can hold one chosen
    ///     synchronous advance between its reservation and its install. <c>SetFloor</c> is Marten's own; its two
    ///     advances come through <see cref="AdvanceToNextHi" /> here, which reports when an install is under way so
    ///     the test can tell that it is blocked.
    /// </summary>
    private sealed class HeldAdvanceSequence: HiloSequence
    {
        private readonly IMartenDatabase _database;
        private readonly string _schema;
        private volatile bool _holdNextSynchronousAdvance;
        private readonly ManualResetEventSlim _release = new();

        private volatile bool _watchInstalls;
        private volatile Thread? _installingThread;
        private int _installsStarted;
        private int _installsFinished;

        public HeldAdvanceSequence(IMartenDatabase database, StoreOptions options, string entityName)
            : base(database, options, entityName, new HiloSettings { MaxLo = 2 })
        {
            _database = database;
            _schema = options.DatabaseSchemaName;
        }

        public TaskCompletionSource<long> Reserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HoldNextSynchronousAdvance() => _holdNextSynchronousAdvance = true;

        public void WatchInstallsFromSetFloor() => _watchInstalls = true;

        public void Release() => _release.Set();

        protected override void AdvanceToNextHiSync()
        {
            using var conn = _database.CreateConnection();
            conn.Open();
            var hi = (long)nextHiCommand(conn).ExecuteScalar()!;

            if (_holdNextSynchronousAdvance)
            {
                _holdNextSynchronousAdvance = false;
                Reserved.TrySetResult(hi);
                _release.Wait(Timeout).ShouldBeTrue("the test never released the held advance");
            }

            TrySetCurrentHi(hi).ShouldBeTrue();
        }

        public override async Task AdvanceToNextHi(CancellationToken ct = default)
        {
            long hi;
            await using (var conn = _database.CreateConnection())
            {
                await conn.OpenAsync(ct);
                hi = (long)(await nextHiCommand(conn).ExecuteScalarAsync(ct))!;
            }

            if (!_watchInstalls)
            {
                TrySetCurrentHi(hi).ShouldBeTrue();
                return;
            }

            _installingThread = Thread.CurrentThread;
            Interlocked.Increment(ref _installsStarted);
            try
            {
                TrySetCurrentHi(hi).ShouldBeTrue();
            }
            finally
            {
                Interlocked.Increment(ref _installsFinished);
            }
        }

        /// <summary>
        ///     Completes when <paramref name="setFloor" /> has returned, or when one of its installs has started, has
        ///     not finished, and its thread is waiting: blocked on <c>HiloSequenceBase</c>'s lock, which the held
        ///     advance owns. Polls a condition rather than sleeping for a guessed interval.
        /// </summary>
        public async Task<string> WaitForSetFloorToFinishOrBlockAsync(Task setFloor, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (setFloor.IsCompleted)
                {
                    await setFloor;
                    return "finished";
                }

                var finishedBefore = Volatile.Read(ref _installsFinished);
                var started = Volatile.Read(ref _installsStarted);
                var thread = _installingThread;

                if (started > finishedBefore && thread != null
                    && (thread.ThreadState & ThreadState.WaitSleepJoin) != 0
                    && Volatile.Read(ref _installsFinished) == finishedBefore)
                {
                    return "blocked";
                }

                await Task.Delay(5);
            }

            throw new TimeoutException("SetFloor neither finished nor blocked on its install");
        }

        private NpgsqlCommand nextHiCommand(NpgsqlConnection conn)
        {
            var command = conn.CreateCommand();
            command.CommandText = $"select {_schema}.mt_get_next_hi(@entity)";
            command.Parameters.AddWithValue("entity", EntityName);
            return command;
        }
    }
}
