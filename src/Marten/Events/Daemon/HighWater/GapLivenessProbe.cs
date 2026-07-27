using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
/// #4953: evidence that a stale sequence gap might still be filled by a transaction that is alive
/// right now. A sequence number inside a gap was reserved by SOME transaction before the gap was
/// first observed; if that transaction is still running the gap is merely OUTSTANDING (its event
/// will commit later) and must not be skipped. Only when no candidate reserver remains — and the
/// rows are still absent — is the gap proven permanently dead (the reserver rolled back).
/// QuiescentSessionsIgnored counts old open transactions that were ruled OUT as reservers because
/// they have provably executed nothing since before the gap's sequence numbers were allocated
/// (e.g. Wolverine's idle-in-transaction advisory-lock listener sessions).
/// </summary>
internal record GapLiveness(long OldLockHolders, long OlderTransactions, long OlderWriteXids,
    long QuiescentSessionsIgnored = 0)
{
    public bool IndicatesLiveReserver => OldLockHolders > 0 || OlderTransactions > 0 || OlderWriteXids > 0;

    public override string ToString()
    {
        return
            $"mt_events write locks held by transactions from before the gap: {OldLockHolders}, open transactions from before the gap: {OlderTransactions}, in-progress write transaction ids from before the gap: {OlderWriteXids}, idle-in-transaction sessions ruled out as reservers: {QuiescentSessionsIgnored}";
    }
}

/// <summary>
/// Single-statement probe for <see cref="GapLiveness"/>, fenced to the moment a stuck gap was first
/// observed (server-side transaction_timestamp + the pg_current_snapshot xmax recorded with it):
///
/// 1. <c>pg_locks</c>: granted RowExclusiveLock on the mt_events lineage (parent + partitions) held
///    by a transaction that began at or before the observation. An in-flight INSERT holds this lock
///    until commit/abort, and pg_locks is fully visible to every role.
/// 2. <c>pg_stat_activity</c>: any client-backend transaction in this database that began at or
///    before the observation. This is the only signal that covers a reserver which called nextval
///    but has not yet issued its first INSERT (no lock, possibly no xid yet). Cross-role sessions
///    hide xact_start from unprivileged viewers (rows show query = '&lt;insufficient privilege&gt;'),
///    so in mixed-role deployments without pg_read_all_stats this clause only sees the daemon's own
///    role — the common single-role deployment sees everything.
/// 3. <c>pg_snapshot_xip(pg_current_snapshot())</c>: any in-progress write transaction id below the
///    xmax recorded at observation. Purely MVCC data — visible regardless of role/privileges — and
///    covers cross-role writers that the redacted pg_stat_activity clause cannot see.
///
/// The allocation fence rules candidate reservers out by proof rather than wall clock: the fence is
/// the latest server time at which the detector observed the event sequence's reserved last_value at
/// or below the stuck mark, so every sequence number inside the gap was allocated strictly AFTER the
/// fence (sequence allocation is monotone; even a cached nextval block requires a statement at
/// block-grab time). A session sitting in 'idle in transaction' whose last state change predates the
/// fence has executed no statement since — it cannot have called nextval for any gap sequence and is
/// excluded from all three clauses. This is what keeps permanently-idle open transactions
/// (Wolverine's advisory-lock listener sessions, leaked ORM sessions, monitoring agents) from
/// holding the gate forever, while a genuine reserver — which by definition executed nextval after
/// the fence, bumping its state_change — is always kept. Without a fence (detector restarted
/// mid-gap, per-tenant path) nothing is excluded: exactly the unfenced #4953 behavior.
///
/// All the sources are proc-array/lock-table scans (monitoring-grade cost) and only run while a
/// stale gap actually exists, at high-water poll cadence.
/// </summary>
internal class GapLivenessProbe: ISingleQueryHandler<GapLiveness>
{
    private readonly EventGraph _graph;
    private readonly DateTimeOffset _gapFirstObserved;
    private readonly long _xmaxAtObservation;
    private readonly DateTimeOffset? _allocationFence;

    public GapLivenessProbe(EventGraph graph, DateTimeOffset gapFirstObserved, long xmaxAtObservation,
        DateTimeOffset? allocationFence = null)
    {
        _graph = graph;
        _gapFirstObserved = gapFirstObserved;
        _xmaxAtObservation = xmaxAtObservation;
        _allocationFence = allocationFence;
    }

    public NpgsqlCommand BuildCommand()
    {
        // quiescent = sessions provably idle since before the gap's sequence numbers were allocated
        // (see the class doc). state/state_change are redacted for cross-role viewers without
        // pg_read_all_stats — a redacted NULL never matches, degrading to "not excluded", which errs
        // toward holding. The strict < keeps a reserver whose state_change could theoretically tie
        // the fence timestamp. The xip exclusion maps 64-bit xid8 values back to 32-bit backend_xid
        // via mod 2^32 (same epoch by construction — both are in progress right now).
        var sql = @"
with quiescent as (
  select a.pid, a.backend_xid
    from pg_stat_activity a
   where a.pid <> pg_backend_pid()
     and a.state in ('idle in transaction', 'idle in transaction (aborted)')
     and a.state_change < :fence
)
select
  (select count(*)
     from pg_locks l
     join pg_stat_activity a on a.pid = l.pid
    where l.locktype = 'relation'
      and l.granted
      and l.mode = 'RowExclusiveLock'
      and l.pid <> pg_backend_pid()
      and l.database = (select d.oid from pg_database d where d.datname = current_database())
      and a.xact_start <= :first_observed
      and l.pid not in (select q.pid from quiescent q)
      and l.relation in (select c.oid
                           from pg_class c
                           join pg_namespace n on n.oid = c.relnamespace
                          where n.nspname = :schema
                            and c.relname like 'mt_events%'
                            and c.relkind in ('r', 'p'))) as old_lock_holders,
  (select count(*)
     from pg_stat_activity a
    where a.datname = current_database()
      and a.pid <> pg_backend_pid()
      and a.backend_type = 'client backend'
      and a.xact_start is not null
      and a.xact_start <= :first_observed
      and a.pid not in (select q.pid from quiescent q)) as older_transactions,
  (select count(*)
     from pg_snapshot_xip(pg_current_snapshot()) as xip(xid)
    where xip.xid::text::bigint < :xmax0
      and mod(xip.xid::text::bigint, 4294967296) not in
          (select q.backend_xid::text::bigint from quiescent q where q.backend_xid is not null)) as older_write_xids,
  (select count(*)
     from pg_stat_activity a
    where a.datname = current_database()
      and a.pid <> pg_backend_pid()
      and a.backend_type = 'client backend'
      and a.xact_start is not null
      and a.xact_start <= :first_observed
      and a.pid in (select q.pid from quiescent q)) as quiescent_ignored
".Trim();

        var command = new NpgsqlCommand(sql);
        command.AddNamedParameter("first_observed", _gapFirstObserved);
        command.AddNamedParameter("schema", _graph.DatabaseSchemaName);
        command.AddNamedParameter("xmax0", _xmaxAtObservation);
        // No fence ⇒ nothing qualifies as quiescent ⇒ exactly the unfenced behavior
        command.AddNamedParameter("fence", _allocationFence ?? DateTimeOffset.MinValue);

        return command;
    }

    public async Task<GapLiveness> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return new GapLiveness(0, 0, 0);
        }

        return new GapLiveness(
            await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false),
            await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false),
            await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false),
            await reader.GetFieldValueAsync<long>(3, token).ConfigureAwait(false));
    }
}
