using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

internal class GapDetector: ISingleQueryHandler<long?>
{
    private readonly EventGraph _graph;

    public GapDetector(EventGraph graph)
    {
        _graph = graph;
    }

    public long Start { get; set; }

    /// <summary>
    /// #4964: when true (the Normal high-water detection path), hold at <see cref="Start"/> instead of
    /// advancing over a LEADING gap — a hole in the sequence immediately above <see cref="Start"/> whose
    /// first committed row sits beyond <c>Start + 1</c>. The interior-gap check only compares
    /// consecutive VISIBLE rows, so it cannot see a gap when <see cref="Start"/> itself is a hole (there is
    /// no visible row at <see cref="Start"/> to pair with the first row above the gap); it would then fall
    /// through to <c>max(seq_id)</c> and advance the Normal high-water mark over a committed-but-not-yet-
    /// visible event with no trace. Holding here keeps the Normal mark from ever crossing an unseen event:
    /// a still-in-flight append fills the gap and the next poll advances normally, while a genuinely
    /// permanent hole (a rolled-back append) is skipped — and recorded — by the SafeZone path once the mark
    /// has been stale past the threshold. The SafeZone path leaves this false so it keeps skipping forward.
    /// </summary>
    public bool HoldBeforeLeadingGap { get; set; }

    public NpgsqlCommand BuildCommand()
    {
        // #4953: this MUST stay a single SQL statement. Under READ COMMITTED every statement in a
        // command takes its own snapshot, so splitting the leading-gap probe, the interior-gap query,
        // and the max() fallback across statements lets commits that land mid-command defeat the gap
        // checks — the fallback would then advance the mark over a sequence number whose reserving
        // transaction is still in flight, silently skipping that event once it commits. One statement
        // = one snapshot = the three readings are always mutually consistent.
        var sql = $@"
select
  (select min(seq_id) from {_graph.DatabaseSchemaName}.mt_events where seq_id > :start) as first_after,
  (select seq_id
   from (select seq_id, lead(seq_id) over (order by seq_id) as next_seq
         from {_graph.DatabaseSchemaName}.mt_events where seq_id >= :start) gaps
   where next_seq is not null and next_seq - seq_id > 1
   order by seq_id
   limit 1) as gap_edge,
  (select max(seq_id) from {_graph.DatabaseSchemaName}.mt_events where seq_id >= :start) as max_seq
".Trim();
        var command = new NpgsqlCommand(sql);
        command.AddNamedParameter("start", Start);

        return command;
    }

    public async Task<long?> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        long? firstAfterStart = await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        long? gapEdge = await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
        long? maxSeq = await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false);

        // (0) Leading-gap hold: the lowest committed sequence strictly above Start sits beyond
        // Start + 1, so a gap the interior-gap query cannot see lies immediately above Start.
        if (HoldBeforeLeadingGap && firstAfterStart.HasValue && firstAfterStart.Value > Start + 1)
        {
            return Start;
        }

        // (1) The first interior sequence gap at or after Start
        if (gapEdge.HasValue)
        {
            return gapEdge;
        }

        // (2) No gap — use the latest committed sequence
        return maxSeq;
    }
}
