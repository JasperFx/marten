#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// One captured-and-bumped tag-version row carried from FetchForWritingByTags
/// through to SaveChangesAsync.
/// </summary>
/// <param name="TagTable">
///     The tag type's <see cref="ITagTypeRegistration.TableSuffix"/> — matches the
///     <c>mt_event_tag_{suffix}</c> table name and is the discriminator stored in
///     <c>mt_dcb_tag_version.tag_table</c>.
/// </param>
/// <param name="TagValue">Canonical string form of the tag value (see <see cref="TagValueStringifier"/>).</param>
/// <param name="CapturedVersion">The version observed at fetch time. The save's UPDATE WHERE version = $captured is the optimistic check.</param>
internal readonly record struct DcbTagVersionEntry(string TagTable, string TagValue, long CapturedVersion);

/// <summary>
/// Storage operation that enforces the DCB tag boundary by bumping the
/// <c>mt_dcb_tag_version</c> rows captured at fetch time. Replaces the racy
/// SELECT-EXISTS over <c>mt_events</c> that <see cref="AssertDcbConsistency"/>
/// emitted. Each UPDATE is the serialization point: at READ COMMITTED, the
/// row-level lock + <c>version = $captured</c> predicate converts what was a
/// predicate read into a row-level write conflict. Fixes #4591.
/// </summary>
/// <remarks>
/// Multi-tag queries emit one UPDATE per (tag_table, tag_value) tuple. The
/// tuples are sorted by (tag_table, tag_value) before SQL is built so two
/// concurrent appenders touching the same tag set acquire locks in identical
/// order — no risk of deadlock from cross-locking.
/// </remarks>
internal class DcbTagVersionAssertion: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly EventTagQuery _query;
    private readonly long _lastSeenSequence;
    private readonly IReadOnlyList<DcbTagVersionEntry> _orderedEntries;

    public DcbTagVersionAssertion(
        EventGraph events,
        EventTagQuery query,
        long lastSeenSequence,
        IReadOnlyList<DcbTagVersionEntry> capturedEntries)
    {
        _events = events;
        _query = query;
        _lastSeenSequence = lastSeenSequence;

        // Sort once, here — both ConfigureCommand and PostprocessAsync iterate
        // in the same order, and the deterministic order is what keeps two
        // concurrent appenders touching the same tag rows from deadlocking.
        var sorted = new DcbTagVersionEntry[capturedEntries.Count];
        for (var i = 0; i < capturedEntries.Count; i++) sorted[i] = capturedEntries[i];
        Array.Sort(sorted, static (a, b) =>
        {
            var byTable = string.CompareOrdinal(a.TagTable, b.TagTable);
            return byTable != 0 ? byTable : string.CompareOrdinal(a.TagValue, b.TagValue);
        });
        _orderedEntries = sorted;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var schema = _events.DatabaseSchemaName;
        var tenantId = session.TenantId;

        for (var i = 0; i < _orderedEntries.Count; i++)
        {
            // StartNewCommand (not `; <sql>`) so Npgsql sends each statement
            // as a separate batched command — multiple `;`-separated
            // statements in a single prepared statement raise Postgres
            // SQLSTATE 42601.
            if (i > 0) builder.StartNewCommand();

            var entry = _orderedEntries[i];

            // INSERT … ON CONFLICT DO UPDATE WHERE handles the two cases the
            // fetch path delegates to us:
            //   - captured = 0 + row missing → INSERT(version=1) succeeds; first save wins.
            //   - row exists → ON CONFLICT branch; the WHERE filters to captured-match
            //     only, so any save that observed a stale version returns no row.
            // INSERT … ON CONFLICT waits on the conflicting row's xact, so two
            // first-time creators serialize on the unique-PK insert path the same
            // way subsequent versioned-update saves serialize on the row lock.
            builder.Append("insert into ");
            builder.Append(schema);
            builder.Append(".mt_dcb_tag_version (tag_table, tag_value, tenant_id, version) values (");
            builder.AppendParameter(entry.TagTable);
            builder.Append(", ");
            builder.AppendParameter(entry.TagValue);
            builder.Append(", ");
            builder.AppendParameter(tenantId);
            builder.Append(", ");
            builder.AppendParameter(entry.CapturedVersion + 1);
            // ON CONFLICT DO UPDATE references the existing row via the
            // unqualified table name (`mt_dcb_tag_version.version`) — Postgres
            // does not accept the schema prefix in this clause.
            builder.Append(") on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version + 1 where mt_dcb_tag_version.version = ");
            builder.AppendParameter(entry.CapturedVersion);
            builder.Append(" returning 1");
        }
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        var conflictDetected = false;

        for (var i = 0; i < _orderedEntries.Count; i++)
        {
            if (i > 0)
            {
                // Advance past the previous statement's result set. The outer
                // OperationPage advances past the LAST result set on its own.
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }

            var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasRow)
            {
                conflictDetected = true;
                // Keep iterating so we consume the remaining result sets — the
                // batch protocol requires every statement's result set to be
                // walked before the next operation can read its own.
            }
        }

        if (conflictDetected)
        {
            exceptions.Add(new DcbConcurrencyException(_query, _lastSeenSequence));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
