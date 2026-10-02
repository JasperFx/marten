#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Medallion.Threading.Postgres;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marten.Events.Daemon.Coordination;

public sealed class AdvisoryLockOptions
{
    /// <summary>
    ///     When true, <see cref="AdvisoryLock.HasLock" /> reports false once the connection holding the lock is lost.
    /// </summary>
    public bool LockMonitoringEnabled { get; set; }

    /// <summary>
    ///     When true, each lock is held by a transaction on its own connection. When false (the default), locks are
    ///     session-scoped and multiplexed so that several held locks share one connection.
    /// </summary>
    public bool TransactionalLockEnabled { get; set; }

    /// <summary>
    ///     How long <see cref="AdvisoryLock.DisposeAsync" /> will wait for the held locks to be released before it
    ///     gives up waiting and leaves the releases running in the background. See the remarks on
    ///     <see cref="AdvisoryLock.DisposeAsync" /> for why a shutdown release needs a bound at all (#5567).
    ///     <para>
    ///     A non-positive value or <see cref="Timeout.InfiniteTimeSpan" /> opts out of the bound and waits
    ///     for every release, matching how JasperFx reads <c>DaemonSettings.StopAndDrainTimeout</c>, which is
    ///     what Marten feeds this from.
    ///     </para>
    /// </summary>
    public TimeSpan ReleaseTimeout { get; set; } = 5.Seconds();
}

/// <summary>
///     PostgreSQL implementation of <see cref="IAdvisoryLock" />, used for the async daemon's
///     <c>HotCold</c> leader election.
/// </summary>
/// <remarks>
///     9.46 (#5567): lifted out of <c>Weasel.Postgresql</c> into Marten so that the shutdown behaviour
///     below could be fixed without a cross-repo release. The intent is to move it back to Weasel once
///     it has settled, so keep it free of anything Marten-specific.
/// </remarks>
public class AdvisoryLock: IAdvisoryLock
{
    private readonly string _databaseName;
    private readonly AdvisoryLockOptions _options;
    private readonly ILogger _logger;
    private readonly NpgsqlDataSource _dataSource;

    // Guards _handles and _disposed. Acquire, release and disposal can run concurrently, and storing a newly
    // acquired handle must be atomic with disposal (see TryAttainLockAsync).
    private readonly object _handlesLock = new();
    private readonly Dictionary<int, PostgresDistributedLockHandle> _handles = new();
    private readonly LightweightCache<int, PostgresDistributedLock> _distributedLockProviders;
    private bool _disposed;

    public AdvisoryLock(NpgsqlDataSource dataSource, ILogger logger, string databaseName, AdvisoryLockOptions options)
    {
        _logger = logger;
        _dataSource = EnsurePrimaryWhenMultiHost(dataSource);

        _distributedLockProviders = new LightweightCache<int, PostgresDistributedLock>(
            (lockId => new PostgresDistributedLock(new PostgresAdvisoryLockKey(lockId),
                EnsurePrimaryWhenMultiHost(dataSource), builder =>
                {
                    // Multiplexing can't be combined with transaction-scoped locks
                    if (options.TransactionalLockEnabled)
                    {
                        builder.UseTransaction();
                    }
                    else
                    {
                        builder.UseMultiplexing();
                    }
                })));
        _databaseName = databaseName;
        _options = options;
    }

    private bool IsDisposed
    {
        get
        {
            lock (_handlesLock)
            {
                return _disposed;
            }
        }
    }

    private static NpgsqlDataSource EnsurePrimaryWhenMultiHost(NpgsqlDataSource source)
    {
        if (source is NpgsqlMultiHostDataSource multiHostDataSource)
            return multiHostDataSource.WithTargetSession(TargetSessionAttributes.ReadWrite);

        return source;
    }

    public bool HasLock(int lockId)
    {
        PostgresDistributedLockHandle? handle;
        lock (_handlesLock)
        {
            if (!_handles.TryGetValue(lockId, out handle))
            {
                return false;
            }
        }

        if (_options.LockMonitoringEnabled)
        {
            return !handle.HandleLostToken.IsCancellationRequested;
        }

        return true;
    }

    /// <summary>
    ///     Attempt to attain the advisory lock with the given identifier.
    /// </summary>
    /// <returns>True when the lock was attained by this node, false when it is held elsewhere.</returns>
    /// <exception cref="ObjectDisposedException">
    ///     Thrown when the underlying <see cref="NpgsqlDataSource" /> has already been disposed. This is terminal:
    ///     the lock latches itself disposed, and every later call returns false without touching the dead pool.
    /// </exception>
    public async Task<bool> TryAttainLockAsync(int lockId, CancellationToken token)
    {
        // Never start an acquire once disposal has begun: the data source may be shutting down with the host
        if (IsDisposed) return false;

        try
        {
            var locker = _distributedLockProviders[lockId];
            var handle = await locker.TryAcquireAsync(cancellationToken: token).ConfigureAwait(false);
            if (handle is null) return false;

            // DisposeAsync can drain _handles while this acquire is in flight. Storing under the same lock means
            // either the drain disposes this handle, or this call sees the disposal and disposes it itself, so a
            // granted lock is never left held for the life of the process.
            PostgresDistributedLockHandle? orphaned = null;
            var stored = false;

            lock (_handlesLock)
            {
                if (_disposed)
                {
                    orphaned = handle;
                }
                else
                {
                    // An existing handle here is one whose connection was lost, so dispose it too
                    _handles.Remove(lockId, out orphaned);
                    _handles[lockId] = handle;
                    stored = true;
                }
            }

            if (orphaned is not null)
            {
                await disposeHandleSafelyAsync(orphaned).ConfigureAwait(false);
            }

            return stored;
        }
        catch (ObjectDisposedException)
        {
            // A disposed data source never comes back. Latch disposed so later polls return false without touching
            // the dead pool, and rethrow so the projection coordinator can end its leadership loop.
            lock (_handlesLock)
            {
                _disposed = true;
            }

            throw;
        }
        catch (Exception e) when (IsDisposed && e is NpgsqlException or InvalidOperationException)
        {
            // The data source was disposed during the acquire and surfaced as a different exception type
            return false;
        }
    }

    /// <summary>
    ///     Who holds <paramref name="lockId" />, or null when nothing does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="HasLock" /> only answers "does this node", so without this no node — and no
    ///         monitoring tool — could learn which node owns a lock set it does not hold. That is the
    ///         diagnostic an operator needs when a projection agent stops with
    ///         <c>ProgressionProgressOutOfOrderException</c>, which means two processes believe they own
    ///         the same shard (weasel#650).
    ///     </para>
    ///     <para>
    ///         <c>pg_locks</c> reports the advisory key as two 32-bit halves of the 64-bit key Medallion
    ///         builds from the id, so a negative id sign-extends into <c>classid = 0xFFFFFFFF</c> rather
    ///         than 0. Measured against the server for both signs and for both session and transactional
    ///         locks; <c>objsubid</c> is deliberately not matched, so the read survives Medallion moving
    ///         between the two-int and bigint forms of the key.
    ///     </para>
    ///     <para>
    ///         The join to <c>pg_stat_activity</c> is a LEFT join: a non-superuser sees every row of
    ///         <c>pg_locks</c> but is shown nothing about another user's backend, and a holder whose
    ///         details are hidden is still a holder worth reporting.
    ///     </para>
    /// </remarks>
    public async Task<AdvisoryLockHolder?> FindHolderAsync(int lockId, CancellationToken token)
    {
        var key = (long)lockId;
        var classId = (long)(uint)(int)(key >> 32);
        var objId = (long)(uint)(int)key;

        await using var conn = await _dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = """
                          SELECT l.pid, a.application_name, a.client_addr::text, a.backend_start
                          FROM pg_locks l
                          LEFT JOIN pg_stat_activity a ON a.pid = l.pid
                          WHERE l.locktype = 'advisory'
                            AND l.classid::bigint = @classid
                            AND l.objid::bigint = @objid
                            AND l.granted
                          LIMIT 1
                          """;

        cmd.Parameters.AddWithValue("classid", classId);
        cmd.Parameters.AddWithValue("objid", objId);

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        return new AdvisoryLockHolder(lockId)
        {
            SessionId = await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                ? null
                : (await reader.GetFieldValueAsync<int>(0, token).ConfigureAwait(false)).ToString(),
            ApplicationName = await nullableStringAsync(reader, 1, token).ConfigureAwait(false),
            ClientAddress = await nullableStringAsync(reader, 2, token).ConfigureAwait(false),

            // The backend's start, which is the earliest the lock can have been taken: PostgreSQL does
            // not record when an advisory lock was acquired
            HeldSince = await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false),

            // Definitive either way for this instance: the lock is exclusive, so if this instance holds
            // it the single holder is us, and if it does not, the holder is not us
            IsCurrentNode = HasLock(lockId)
        };
    }

    private static async Task<string?> nullableStringAsync(NpgsqlDataReader reader, int ordinal, CancellationToken token)
    {
        return await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<string>(ordinal, token).ConfigureAwait(false);
    }

    public async Task ReleaseLockAsync(int lockId)
    {
        PostgresDistributedLockHandle? handle;
        lock (_handlesLock)
        {
            _handles.Remove(lockId, out handle);
        }

        if (handle is not null)
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Release every held lock. Best-effort and time-bounded: see the remarks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         #5567. A session-scoped lock multiplexes several held locks onto one connection, and Medallion's
    ///         connection monitor — which <see cref="HasLock" /> activates when
    ///         <see cref="AdvisoryLockOptions.LockMonitoringEnabled" /> is on — parks that connection in a
    ///         one-minute wait between checks. Releasing a lock has to take the connection away from the
    ///         monitor, which it does by firing the monitor's state-changed token, but
    ///         <c>ConnectionMonitor.AcquireConnectionLockAsync</c> only fires it while monitoring handles are
    ///         still registered, and a handle drops its monitoring registration BEFORE it runs its
    ///         <c>pg_advisory_unlock</c>. So the last release on a given connection can find the count already
    ///         at zero, fire nothing, and sit in its own two-second retry loop — re-evaluating that same false
    ///         condition — until the monitor's window expires on its own. With two such connections a host stop
    ///         took 120 seconds.
    ///     </para>
    ///     <para>
    ///         Two things follow, and both are implemented here. The releases run concurrently, so a stop pays
    ///         at most one stalled window rather than one per connection. And the wait is bounded by
    ///         <see cref="AdvisoryLockOptions.ReleaseTimeout" />, after which the remaining releases are left to
    ///         finish in the background: the unlock still goes through when the monitor's window ends, and the
    ///         process is usually exiting anyway, which closes the connections and drops the session-scoped
    ///         locks regardless. Abandoning the wait delays leadership handover for the rest of that window; it
    ///         does not leak a lock.
    ///     </para>
    ///     <para>
    ///         A release that overruns is logged. It used to be entirely silent — nothing on this path logs
    ///         unless it throws — which is why a minute-long host stop came with no explanation at all.
    ///     </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        PostgresDistributedLockHandle[] handles;

        lock (_handlesLock)
        {
            // Latch and drain atomically so no handle can be stored after this point (see TryAttainLockAsync)
            _disposed = true;
            handles = _handles.Values.ToArray();
            _handles.Clear();
        }

        if (handles.Length == 0) return;

        var releases = new Task[handles.Length];
        for (var i = 0; i < handles.Length; i++)
        {
            releases[i] = disposeHandleSafelyAsync(handles[i]);
        }

        var all = Task.WhenAll(releases);

        // Same convention as JasperFx's DaemonSettings.StopAndDrainTimeout, which is what feeds this:
        // a non-positive or infinite value opts out of the separate bound rather than meaning
        // "give up immediately".
        if (_options.ReleaseTimeout <= TimeSpan.Zero || _options.ReleaseTimeout == Timeout.InfiniteTimeSpan)
        {
            await all.ConfigureAwait(false);
            return;
        }

        // The token only stops the timer once the releases have won the race -- it never cancels a
        // release, which has to run to completion for the lock to actually come off the session.
        using var timer = new CancellationTokenSource();
        var finished = await Task.WhenAny(all, Task.Delay(_options.ReleaseTimeout, timer.Token))
            .ConfigureAwait(false);

        if (ReferenceEquals(finished, all))
        {
            await timer.CancelAsync().ConfigureAwait(false);

            // Observe the faults -- disposeHandleSafelyAsync already swallows them, so this only unwraps
            await all.ConfigureAwait(false);
            return;
        }

        var outstanding = releases.Count(x => !x.IsCompleted);
        _logger.LogWarning(
            "Timed out after {Timeout} waiting to release {Outstanding} of {Total} advisory locks for database {Identifier}. The releases are still running and the locks will be released when they complete, but leadership of those projections cannot move to another node until then. See https://github.com/JasperFx/marten/issues/5567",
            _options.ReleaseTimeout, outstanding, handles.Length, _databaseName);

        // Don't leave the abandoned releases as unobserved faulted tasks
        _ = all.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
    }

    private async Task disposeHandleSafelyAsync(PostgresDistributedLockHandle handle)
    {
        try
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The connection or data source is already closed (ObjectDisposedException derives from this)
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to dispose of advisory locks for database {Identifier}", _databaseName);
        }
    }
}
