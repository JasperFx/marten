#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Core.Sequences;
using Weasel.Postgresql;

namespace Marten.Schema.Identity.Sequences;

// 9.0 (#4527 dedupe): the Hi-Lo client-side arithmetic + hi/lo state now lives in
// Weasel.Core.Sequences.HiloSequenceBase (line-for-line identical to Marten's prior
// implementation). This subclass keeps only the PostgreSQL I/O — the mt_get_next_hi
// stored function and the mt_hilo floor update.
//
// 10.0 (#5862): ids are still issued synchronously from Store(), but the NEXT block is
// reserved ahead of time with an asynchronous round trip, so in steady state an exhausted
// block is replaced without blocking the storing thread. See the remarks on the class.

/// <summary>
///     The PostgreSQL Hi-Lo sequence behind Marten's numeric and identity-key document ids.
/// </summary>
/// <remarks>
///     <para>
///     <b>Block prefetching (#5862).</b> Every time a block (a "hi") is installed, this sequence
///     immediately starts reserving the following one in the background with an asynchronous
///     <c>mt_get_next_hi</c> call. When <c>Store()</c> exhausts the current block, the prefetched
///     block is swapped in with no database round trip. If there is no prefetch, it failed, or it has
///     not landed yet, the sequence falls back to the old synchronous fetch straight away, so ids are
///     still assigned the moment a document is stored. The synchronous path never waits on the
///     prefetch (#5969).
///     </para>
///     <para>
///     At most one prefetch is in flight per sequence. A prefetch never faults: a failure is
///     captured, logged and counted, and the next exhausted block simply uses the synchronous fetch.
///     </para>
///     <para>
///     Consequences: a process reserves up to one block more than it uses, so ids are lost (gaps)
///     when it shuts down holding an unused prefetched block. Ids issued by one sequence instance are
///     unique and increasing, but are not guaranteed to be contiguous across a block boundary: when
///     a synchronous fallback overtakes a prefetch still in flight, the block the prefetch reserved is
///     discarded rather than handed out out of order.
///     </para>
/// </remarks>
public class HiloSequence: HiloSequenceBase
{
    private readonly IMartenDatabase _database;
    private readonly StoreOptions _options;

    // #5862: the one prefetch slot. Read and replaced only under _prefetchLock, which is never
    // held across an await or a database call. The task never faults: a failed fetch yields -1.
    private readonly Lock _prefetchLock = new();
    private Task<long>? _prefetch;

    private long _synchronousFetches;
    private long _prefetchedBlocks;
    private long _prefetchFailures;
    private long _discardedPrefetches;
    private Exception? _lastPrefetchFailure;
    private ILogger? _logger;

    public HiloSequence(IMartenDatabase database, StoreOptions options, string entityName, HiloSettings settings)
        : base(entityName, settings)
    {
        _database = database;
        _options = options;
    }

    private DbObjectName GetNextFunction => new PostgresqlObjectName(_options.DatabaseSchemaName, "mt_get_next_hi");

    /// <summary>
    ///     How many blocks were fetched with a blocking round trip from the synchronous id path: the
    ///     first block, and every block that was needed before its prefetch had completed.
    /// </summary>
    internal long SynchronousFetchCount => Interlocked.Read(ref _synchronousFetches);

    /// <summary>
    ///     How many blocks were installed from a completed prefetch, with no round trip.
    /// </summary>
    internal long PrefetchedBlockCount => Interlocked.Read(ref _prefetchedBlocks);

    /// <summary>
    ///     How many background prefetches failed. Each failure only means the next block is fetched
    ///     synchronously.
    /// </summary>
    internal long PrefetchFailureCount => Interlocked.Read(ref _prefetchFailures);

    /// <summary>
    ///     How many successfully prefetched blocks were thrown away unused, because a synchronous
    ///     fallback had already installed a later block, or <see cref="SetFloor" /> invalidated them.
    /// </summary>
    internal long DiscardedPrefetchCount => Interlocked.Read(ref _discardedPrefetches);

    internal Exception? LastPrefetchFailure => Volatile.Read(ref _lastPrefetchFailure);

    /// <summary>
    ///     Completes when the prefetch currently in flight (if any) has finished, successfully or not.
    ///     Never throws. For tests and orderly shutdown only.
    /// </summary>
    internal Task WaitForPrefetchAsync()
    {
        lock (_prefetchLock)
        {
            return _prefetch ?? Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Move the database's hilo row so that ids above <paramref name="floor" /> are issued from now on,
    ///     and install a block reserved above it.
    /// </summary>
    /// <remarks>
    ///     Once this returns, no id at or below the floor is issued, even by a <c>NextLong()</c> or
    ///     <c>NextLongAsync()</c> that was part way through an advance when the floor moved and comes back
    ///     with a block reserved under the old floor. This does not take the base class's advance locks, so it
    ///     relies on two rules. A prefetch reserved before the row was updated is discarded rather than
    ///     installed. And an advance's block that lands after another install, here the block installed by
    ///     this method, and is not above it is dropped as a gap (weasel#840). The block this method reserves
    ///     above the floor follows the same rule (weasel#849, #6058): when an advance reserved a higher block
    ///     after the row moved and installed it first, the floor's block is dropped, so ids never step back.
    /// </remarks>
    public override async Task SetFloor(long floor)
    {
        var numberOfPages = (long)Math.Ceiling((double)floor / MaxLo);
        var updateSql =
            $"update {_options.DatabaseSchemaName}.mt_hilo set hi_value = :floor where entity_name = :name";

        // A block reserved ahead of the floor change would hand out ids below the new floor
        discardPrefetch();

        // This guarantees that the hilo row exists
        installHi(await FetchNextHiAsync(CancellationToken.None).ConfigureAwait(false));

        await using var conn = _database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await conn.CreateCommand(updateSql)
            .With("floor", numberOfPages)
            .With("name", EntityName)
            .ExecuteNonQueryAsync().ConfigureAwait(false);

        // #6058 (weasel#849): counted once the row has moved. A block installed from here on, before the floor's
        // own, was reserved above the floor as well, and when it is also above the floor's block, the floor's
        // block is dropped as a gap instead of handing out ids behind it.
        var installsBefore = InstallCount;

        // Anything prefetched while the floor was moving may have been reserved under the old floor
        discardPrefetch();

        // And again to get it where we need it to be
        TrySetCurrentHi(await FetchNextHiAsync(CancellationToken.None).ConfigureAwait(false), installsBefore);
        startPrefetch();
    }

    /// <summary>
    ///     Install the next block asynchronously. Waits for, and uses, the block already being
    ///     prefetched when there is one, then starts prefetching the block after it.
    /// </summary>
    public override async Task AdvanceToNextHi(CancellationToken ct = default)
    {
        Task<long>? pending;
        lock (_prefetchLock)
        {
            pending = _prefetch;
        }

        if (pending is { IsCompleted: false })
        {
            // The asynchronous path can afford to wait for the block that is already on its way.
            // The prefetch task never faults, so this only throws if ct is cancelled, and then
            // the prefetch stays in its slot for the next caller.
            await pending.WaitAsync(ct).ConfigureAwait(false);
        }

        if (!tryInstallPrefetchedBlock())
        {
            installHi(await FetchNextHiAsync(ct).ConfigureAwait(false));
        }

        startPrefetch();
    }

    /// <summary>
    ///     Install the next block on the synchronous id path (<c>Store()</c>). Uses the prefetched block
    ///     when it has already arrived. Otherwise, whether the prefetch is still in flight, failed, or was
    ///     never started, fetches a block of its own with a blocking round trip, straight away.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     #5969: this never waits on the prefetch. A thread blocked in <c>Store()</c> is usually a
    ///     thread-pool thread, and the prefetch is asynchronous work that needs a thread-pool thread to
    ///     finish, so waiting for it (as #5938 briefly did, for up to five seconds) is exactly how a burst of
    ///     synchronous writers starves the pool. The blocking fetch needs no other thread.
    ///     </para>
    ///     <para>
    ///     Racing the prefetch for the <c>mt_hilo</c> row is safe since #5938 made <c>mt_get_next_hi</c> a
    ///     single atomic increment: both callers get a block, neither gets -1. If the prefetch turns out to
    ///     have reserved the lower block, it is discarded when it is next looked at rather than issued behind
    ///     a later one, which costs a gap in the ids and never a duplicate.
    ///     </para>
    /// </remarks>
    protected override void AdvanceToNextHiSync()
    {
        if (!tryInstallPrefetchedBlock())
        {
            Interlocked.Increment(ref _synchronousFetches);
            installHi(FetchNextHiSync());
        }

        startPrefetch();
    }

    /// <summary>
    ///     Reserve the next "hi" with one asynchronous call to <c>mt_get_next_hi</c>, retrying while
    ///     the function reports contention. Does not change this sequence's state.
    /// </summary>
    internal virtual async Task<long> FetchNextHiAsync(CancellationToken ct)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        for (var attempts = 0; attempts < Settings.MaxAdvanceToNextHiAttempts; attempts++)
        {
            var command = GetNexFunctionCommand(conn);
            var raw = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

            var hi = Convert.ToInt64(raw);
            if (hi >= 0)
            {
                return hi;
            }
        }

        throw new HiloSequenceAdvanceToNextHiAttemptsExceededException();
    }

    /// <summary>
    ///     Blocking counterpart of <see cref="FetchNextHiAsync" />, used only as the fallback when a
    ///     block is needed before its prefetch has completed.
    /// </summary>
    internal virtual long FetchNextHiSync()
    {
        using var conn = _database.CreateConnection();
        conn.Open();

        for (var attempts = 0; attempts < Settings.MaxAdvanceToNextHiAttempts; attempts++)
        {
            var command = GetNexFunctionCommand(conn);
            var raw = command.ExecuteScalar();

            var hi = Convert.ToInt64(raw);
            if (hi >= 0)
            {
                return hi;
            }
        }

        throw new HiloSequenceAdvanceToNextHiAttemptsExceededException();
    }

    private void installHi(long hi)
    {
        TrySetCurrentHi(hi);
    }

    private bool tryInstallPrefetchedBlock()
    {
        long hi;
        lock (_prefetchLock)
        {
            var pending = _prefetch;
            if (pending is not { IsCompleted: true })
            {
                return false;
            }

            _prefetch = null;
            hi = pending.Result; // never faults, so this never throws or blocks
        }

        if (hi < 0)
        {
            // The prefetch failed; it was logged and counted when it did
            return false;
        }

        if (CurrentHi >= 0 && hi <= CurrentHi)
        {
            // A synchronous fallback overtook this prefetch in the database and installed a later
            // block. Handing this one out now would issue ids out of order, so it is a gap instead.
            Interlocked.Increment(ref _discardedPrefetches);
            return false;
        }

        installHi(hi);
        Interlocked.Increment(ref _prefetchedBlocks);
        return true;
    }

    private void startPrefetch()
    {
        lock (_prefetchLock)
        {
            if (_prefetch != null)
            {
                return;
            }

            // Task.Run so the fetch never starts on the caller's thread (which may hold the
            // sequence gate) and never captures the caller's SynchronizationContext
            _prefetch = Task.Run(prefetchAsync);
        }
    }

    private void discardPrefetch()
    {
        Task<long>? pending;
        lock (_prefetchLock)
        {
            pending = _prefetch;
            _prefetch = null;
        }

        if (pending is { IsCompletedSuccessfully: true } && pending.Result >= 0)
        {
            Interlocked.Increment(ref _discardedPrefetches);
        }

        // A prefetch still in flight completes on its own and is simply never read. It cannot
        // fault, so there is nothing to observe.
    }

    private async Task<long> prefetchAsync()
    {
        try
        {
            return await FetchNextHiAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Never let a prefetch fault: a faulted task nobody awaits would surface as an
            // UnobservedTaskException, and a cached fault would poison every later block. The
            // synchronous fallback takes over for the next block instead.
            Volatile.Write(ref _lastPrefetchFailure, e);
            Interlocked.Increment(ref _prefetchFailures);

            try
            {
                logger().LogWarning(e,
                    "Prefetching the next HiLo block for '{EntityName}' failed. The next block will be fetched synchronously instead",
                    EntityName);
            }
            catch (Exception)
            {
                // Logging must never turn a handled failure into an unhandled one
            }

            return -1;
        }
    }

    private ILogger logger()
    {
        return _logger ??= _options.LogFactory?.CreateLogger<HiloSequence>()
                           ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    private NpgsqlCommand GetNexFunctionCommand(NpgsqlConnection conn)
    {
        // The function returns -1 when it could not secure the next hi. Since #5938 it is a single
        // atomic increment that never does, but a database that has not had that version applied yet
        // still carries the old compare-and-set, so the retry loops above stay
        return conn.CallFunction(GetNextFunction, "entity")
            .With("entity", EntityName)
            .Returns("next", NpgsqlDbType.Bigint);
    }
}
