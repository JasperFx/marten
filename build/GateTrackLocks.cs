using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Serilog;

/// <summary>
/// #6069: a gate's tracks held across processes, so two LocalGate runs from different worktrees on one
/// Postgres server queue on the fixed-name databases they share instead of running into each other.
/// </summary>
/// <remarks>
/// <para>
/// Bobcat keeps two units on one track apart inside one gate process only. Each track here is also a
/// session-level Postgres advisory lock on the gate's server, taken when a unit holding the track starts
/// and released when that unit finishes, so gates on other worktrees still run side by side and only wait
/// on the databases they share.
/// </para>
/// <para>
/// A unit's locks live on one connection of their own. Closing it releases them, and so does the gate
/// process dying, because Postgres drops a session's advisory locks with the session. Locks are taken in
/// name order, the same order in every gate, so two gates waiting on each other's tracks can't deadlock.
/// Bobcat starts a unit only once its tracks are free in this process, so a unit never holds an advisory
/// lock while it waits for a track in its own gate.
/// </para>
/// <para>
/// Bobcat 1.0.0 has no hook for the end of a unit, so the release is driven by the gate's own log line
/// for it: <c>=== {unit}: ...</c>, written once for every unit that started, whatever its outcome.
/// </para>
/// </remarks>
sealed class GateTrackLocks : IDisposable
{
    const string KeyPrefix = "marten-localgate-track:";

    readonly string _server;
    readonly Dictionary<string, NpgsqlConnection> _held = new(StringComparer.Ordinal);
    readonly object _gate = new();

    public GateTrackLocks(string serverConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            // Advisory locks are scoped to a database, and the fixed names a track stands for are the server's,
            // so every gate takes them in the server's maintenance database, whatever its base database is
            Database = "postgres",

            // The wait for another gate is as long as that gate's unit, so no command timeout, and the
            // connection is never shared through the pool with anything that expects one.
            CommandTimeout = 0,
            Pooling = false,
            ApplicationName = "marten-localgate-track-lock"
        };
        _server = builder.ConnectionString;
    }

    /// <summary>The advisory lock key of a track: the same in every process, unlike string.GetHashCode.</summary>
    public static long KeyFor(string track)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(KeyPrefix + track.ToLowerInvariant()));
        return BitConverter.ToInt64(hash, 0);
    }

    /// <summary>
    /// Take every track of <paramref name="unit"/>, waiting for another gate that holds one. Blocks the
    /// unit's start, which is what the wait is for.
    /// </summary>
    public void Acquire(string unit, IReadOnlyCollection<string> tracks)
    {
        if (tracks.Count == 0) return;

        var conn = new NpgsqlConnection(_server);
        conn.Open();

        try
        {
            foreach (var track in tracks.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
            {
                var key = KeyFor(track);
                if (tryLock(conn, key)) continue;

                Log.Information("LocalGate: {Unit} waits for track {Track}, which a gate in another checkout holds", unit, track);
                using var wait = new NpgsqlCommand("select pg_advisory_lock(@key)", conn);
                wait.Parameters.AddWithValue("key", key);
                wait.ExecuteNonQuery();
                Log.Information("LocalGate: {Unit} has track {Track}", unit, track);
            }
        }
        catch
        {
            conn.Dispose();
            throw;
        }

        lock (_gate)
        {
            // A unit name starts once per gate; a leftover from an unreported finish is released here
            if (_held.Remove(unit, out var stale)) stale.Dispose();
            _held[unit] = conn;
        }
    }

    /// <summary>Release the tracks <paramref name="unit"/> holds, if any.</summary>
    public void Release(string unit)
    {
        NpgsqlConnection conn;
        lock (_gate)
        {
            if (!_held.Remove(unit, out conn)) return;
        }

        // Closing the session releases its advisory locks
        conn.Dispose();
    }

    /// <summary>
    /// Release the tracks of the unit a gate log line reports as finished: <c>=== {unit}: {mark} ...</c>
    /// or <c>=== {unit}: ? {exception}</c>, but not <c>=== {unit}: starting on ...</c>.
    /// </summary>
    public void ReleaseIfFinished(string gateLogLine)
    {
        if (!gateLogLine.StartsWith("=== ", StringComparison.Ordinal)) return;

        string[] holding;
        lock (_gate) holding = _held.Keys.ToArray();

        if (FinishedUnit(gateLogLine, holding) is { } unit) Release(unit);
    }

    /// <summary>The unit of <paramref name="units" /> a gate log line reports as finished, or null.</summary>
    public static string FinishedUnit(string gateLogLine, IEnumerable<string> units)
    {
        if (!gateLogLine.StartsWith("=== ", StringComparison.Ordinal)) return null;

        foreach (var unit in units)
        {
            var prefix = $"=== {unit}: ";
            if (!gateLogLine.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (gateLogLine.AsSpan(prefix.Length).StartsWith("starting on ", StringComparison.Ordinal)) return null;
            return unit;
        }

        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var conn in _held.Values) conn.Dispose();
            _held.Clear();
        }
    }

    static bool tryLock(NpgsqlConnection conn, long key)
    {
        using var cmd = new NpgsqlCommand("select pg_try_advisory_lock(@key)", conn);
        cmd.Parameters.AddWithValue("key", key);
        return (bool)cmd.ExecuteScalar()!;
    }
}
