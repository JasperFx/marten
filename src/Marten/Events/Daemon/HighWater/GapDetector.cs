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
    /// first committed row sits beyond <c>Start + 1</c>. The interior-gap query below only compares
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
        var sql = $@"
select min(seq_id) from {_graph.DatabaseSchemaName}.mt_events where seq_id > :start;
select seq_id
from   (select
               seq_id,
               lead(seq_id)
               over (order by seq_id) as no
        from
               {_graph.DatabaseSchemaName}.mt_events where seq_id >= :start) ct
where  no is not null
  and    no - seq_id > 1
LIMIT 1;
select max(seq_id) from {_graph.DatabaseSchemaName}.mt_events where seq_id >= :start;
".Trim();
        var command = new NpgsqlCommand(sql);
        command.AddNamedParameter("start", Start);

        return command;
    }

    public async Task<long?> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        // (0) Leading-gap probe: the lowest committed sequence strictly above Start. In the Normal path,
        // if it sits beyond Start + 1 there is a gap immediately above Start that the interior-gap query
        // cannot see, so hold at Start rather than fall through to max and silently cross the gap.
        long? firstAfterStart = null;
        if (await reader.ReadAsync(token).ConfigureAwait(false)
            && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            firstAfterStart = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        if (HoldBeforeLeadingGap && firstAfterStart.HasValue && firstAfterStart.Value > Start + 1)
        {
            return Start;
        }

        // (1) If there is a row, this tells us the first interior sequence gap
        await reader.NextResultAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        // (2) use the latest sequence in the event table because there is NO gap
        await reader.NextResultAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        if (!await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        return null;
    }
}
