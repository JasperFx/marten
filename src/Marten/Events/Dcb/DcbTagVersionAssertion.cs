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
/// <param name="Query">The boundary query this row was captured for — carried per row rather than per assertion so a merged assertion can still name the boundary that failed. See #5280.</param>
/// <param name="LastSeenSequence">The event sequence <paramref name="Query"/> had read up to, for the exception message.</param>
internal readonly record struct DcbTagVersionEntry(
    string TagTable,
    string TagValue,
    long CapturedVersion,
    EventTagQuery Query,
    long LastSeenSequence);

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
///
/// #5280: one save carries ONE of these, covering every boundary the session
/// read. A row named by two boundaries — the same query fetched twice, or two
/// overlapping queries — appears once. Two assertions over one row would each
/// carry the same captured version, and the first one's bump would make the
/// second one's <c>where version = $captured</c> miss: a session reporting a
/// concurrency conflict against itself.
/// </remarks>
internal class DcbTagVersionAssertion: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly IReadOnlyList<DcbTagVersionEntry> _orderedEntries;

    public DcbTagVersionAssertion(
        EventGraph events,
        IReadOnlyList<DcbTagVersionEntry> capturedEntries)
    {
        _events = events;

        // Sort once, here — both ConfigureCommand and PostprocessAsync iterate
        // in the same order, and the deterministic order is what keeps two
        // concurrent appenders touching the same tag rows from deadlocking.
        // Sorting the merged set is also what makes that guarantee hold across
        // boundaries: two sessions holding the same pair of boundaries in
        // opposite fetch order would otherwise take the row locks in opposite
        // order too.
        var sorted = new DcbTagVersionEntry[capturedEntries.Count];
        for (var i = 0; i < capturedEntries.Count; i++) sorted[i] = capturedEntries[i];
        Array.Sort(sorted, static (a, b) =>
        {
            var byTable = string.CompareOrdinal(a.TagTable, b.TagTable);
            if (byTable != 0) return byTable;

            var byValue = string.CompareOrdinal(a.TagValue, b.TagValue);
            if (byValue != 0) return byValue;

            // Oldest capture first, so collapsing a duplicate run below keeps the
            // strictest check of the row: every read the session made has to still
            // be valid, not just the most recent one.
            return a.CapturedVersion.CompareTo(b.CapturedVersion);
        });

        _orderedEntries = collapseDuplicateRows(sorted);
    }

    // Duplicates are adjacent after the sort, so one pass over the run keeps the
    // first (oldest captured version) of each (tag_table, tag_value).
    private static DcbTagVersionEntry[] collapseDuplicateRows(DcbTagVersionEntry[] sorted)
    {
        if (sorted.Length < 2) return sorted;

        var kept = 1;
        for (var i = 1; i < sorted.Length; i++)
        {
            var previous = sorted[kept - 1];
            if (sorted[i].TagTable == previous.TagTable && sorted[i].TagValue == previous.TagValue) continue;

            sorted[kept++] = sorted[i];
        }

        if (kept == sorted.Length) return sorted;

        var collapsed = new DcbTagVersionEntry[kept];
        Array.Copy(sorted, collapsed, kept);
        return collapsed;
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
        DcbTagVersionEntry? conflicted = null;

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
                // First loser wins the exception — the row carries the boundary
                // query that captured it, so the message names a query the caller
                // actually made even when the save spans several boundaries.
                conflicted ??= _orderedEntries[i];
                // Keep iterating so we consume the remaining result sets — the
                // batch protocol requires every statement's result set to be
                // walked before the next operation can read its own.
            }
        }

        if (conflicted.HasValue)
        {
            exceptions.Add(new DcbConcurrencyException(conflicted.Value.Query, conflicted.Value.LastSeenSequence));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
