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
/// </summary>
internal record GapLiveness(long OldLockHolders, long OlderTransactions, long OlderWriteXids)
{
    public bool IndicatesLiveReserver => OldLockHolders > 0 || OlderTransactions > 0 || OlderWriteXids > 0;

    public override string ToString()
    {
        return
            $"mt_events write locks held by transactions from before the gap: {OldLockHolders}, open transactions from before the gap: {OlderTransactions}, in-progress write transaction ids from before the gap: {OlderWriteXids}";
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
/// All three are proc-array/lock-table scans (monitoring-grade cost) and only run while a stale gap
/// actually exists, at high-water poll cadence.
/// </summary>
internal class GapLivenessProbe: ISingleQueryHandler<GapLiveness>
{
    private readonly EventGraph _graph;
    private readonly DateTimeOffset _gapFirstObserved;
    private readonly long _xmaxAtObservation;

    public GapLivenessProbe(EventGraph graph, DateTimeOffset gapFirstObserved, long xmaxAtObservation)
    {
        _graph = graph;
        _gapFirstObserved = gapFirstObserved;
        _xmaxAtObservation = xmaxAtObservation;
    }

    public NpgsqlCommand BuildCommand()
    {
        var sql = @"
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
      and a.xact_start <= :first_observed) as older_transactions,
  (select count(*)
     from pg_snapshot_xip(pg_current_snapshot()) as xip(xid)
    where xip.xid::text::bigint < :xmax0) as older_write_xids
".Trim();

        var command = new NpgsqlCommand(sql);
        command.AddNamedParameter("first_observed", _gapFirstObserved);
        command.AddNamedParameter("schema", _graph.DatabaseSchemaName);
        command.AddNamedParameter("xmax0", _xmaxAtObservation);

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
            await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false));
    }
}
