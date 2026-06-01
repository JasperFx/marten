#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// Producer-side bump for the DCB tag-version side table. Every save that appends
/// a tagged event (boundary or not) queues one of these per distinct
/// (tag_table, tag_value) tuple so the side table reflects every commit, not only
/// boundary saves. Without this hook, a plain
/// <c>session.Events.Append(streamId, taggedEvent)</c> would commit silently
/// without invalidating any in-flight DCB boundary that captured the prior
/// version — see #4591.
/// </summary>
/// <remarks>
/// Emits a single multi-row INSERT … ON CONFLICT DO UPDATE statement (mirroring
/// the per-statement <see cref="Marten.Events.Operations.InsertEventTagOperation"/>
/// shape: no RETURNING, empty PostprocessAsync) — that keeps the batch protocol
/// trivial. A previous design used N separate statements with RETURNING which
/// interleaved the batched result sets with neighbouring ops' result sets and
/// shifted reader positions in subtle ways.
/// </remarks>
internal class DcbTagVersionBumpOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly IReadOnlyList<(string TagTable, string TagValue)> _orderedEntries;

    public DcbTagVersionBumpOperation(
        EventGraph events,
        IReadOnlyList<(string TagTable, string TagValue)> entries)
    {
        _events = events;

        // Deterministic order across concurrent appenders → no deadlocks when
        // two saves touch overlapping tag rows. Same rationale as
        // DcbTagVersionAssertion.
        var sorted = new (string, string)[entries.Count];
        for (var i = 0; i < entries.Count; i++) sorted[i] = entries[i];
        Array.Sort(sorted, static (a, b) =>
        {
            var byTable = string.CompareOrdinal(a.Item1, b.Item1);
            return byTable != 0 ? byTable : string.CompareOrdinal(a.Item2, b.Item2);
        });
        _orderedEntries = sorted;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var schema = _events.DatabaseSchemaName;
        var tenantId = session.TenantId;

        // One multi-row INSERT — VALUES list feeds straight into the
        // ON CONFLICT DO UPDATE that bumps each row by 1. Row-level locks are
        // taken in (tag_table, tag_value) sorted order via the index seek, so
        // concurrent appenders touching the same tag set can't deadlock.
        builder.Append("insert into ");
        builder.Append(schema);
        builder.Append(".mt_dcb_tag_version (tag_table, tag_value, tenant_id, version) values ");

        for (var i = 0; i < _orderedEntries.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append("(");

            var tableParam = builder.AppendParameter(_orderedEntries[i].TagTable);
            tableParam.NpgsqlDbType = NpgsqlDbType.Varchar;
            builder.Append(", ");

            var valueParam = builder.AppendParameter(_orderedEntries[i].TagValue);
            valueParam.NpgsqlDbType = NpgsqlDbType.Text;
            builder.Append(", ");

            var tenantParam = builder.AppendParameter(tenantId);
            tenantParam.NpgsqlDbType = NpgsqlDbType.Varchar;
            builder.Append(", 1)");
        }

        // ON CONFLICT DO UPDATE references the existing row via the
        // unqualified table name (`mt_dcb_tag_version.version`) — Postgres
        // does not accept the schema prefix in this clause.
        builder.Append(" on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version + 1");
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        // No RETURNING; mirror InsertEventTagOperation's empty PostprocessAsync.
        // The outer OperationPage advances past our single result set on its own.
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
