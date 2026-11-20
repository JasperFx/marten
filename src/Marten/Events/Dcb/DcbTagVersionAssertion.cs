#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// One captured tag-version row carried from FetchForWritingByTags
/// through to SaveChangesAsync.
/// </summary>
/// <param name="TagTable">
///     The tag type's <see cref="ITagTypeRegistration.TableSuffix"/> — matches the
///     <c>mt_event_tag_{suffix}</c> table name and is the discriminator stored in
///     <c>mt_dcb_tag_version.tag_table</c>.
/// </param>
/// <param name="TagValue">Canonical string form of the tag value (see <see cref="TagValueStringifier"/>).</param>
/// <param name="CapturedVersion">The version observed at fetch time. The save's <c>where version = $captured</c> is the optimistic check.</param>
/// <param name="Query">The boundary query this row was captured for — carried per row rather than per assertion so a merged assertion can still name the boundary that failed. See #5280.</param>
/// <param name="LastSeenSequence">The event sequence <paramref name="Query"/> had read up to, for the exception message.</param>
internal readonly record struct DcbTagVersionEntry(
    string TagTable,
    string TagValue,
    long CapturedVersion,
    EventTagQuery Query,
    long LastSeenSequence);

/// <summary>
/// #6035: what a boundary read when at least one of its tags had no <c>mt_dcb_tag_version</c> row (or a row
/// still at 0), which the save has to look at again: a row can't say whether those events are still there.
/// </summary>
/// <remarks>
/// A tag can have events and no row: events appended before the table existed, tags added with
/// <c>AssignTagWhere</c> before #6078, or per-event-type versions a store recorded only after it changed
/// <see cref="DcbConflictScope"/>. A tombstone of such a stream has no row to move and may not create one,
/// because it is erasing the value, so nothing would tell a boundary that read those events. The save counts
/// them again instead. See <see cref="For"/>.
/// </remarks>
/// <param name="Query">The boundary's query.</param>
/// <param name="TenantId">The tenant the boundary read.</param>
/// <param name="LastSeenSequence">How far the read went.</param>
/// <param name="EventCount">How many events it read.</param>
/// <param name="EventTypeNames">The aggregate's event type filter the read applied, if any.</param>
internal sealed record DcbBoundaryRead(
    EventTagQuery Query,
    string TenantId,
    long LastSeenSequence,
    int EventCount,
    string[]? EventTypeNames)
{
    /// <summary>
    /// Non-null when the boundary read events and one of its tags captured 0. Asks nothing of a boundary
    /// whose tags all had rows, which is the steady state, or of one that read nothing, a first use of the
    /// tag.
    /// </summary>
    public static DcbBoundaryRead? For(IReadOnlyList<DcbTagVersionEntry> entries, string tenantId,
        int eventCount, string[]? eventTypeNames)
    {
        if (eventCount == 0 || entries.Count == 0) return null;

        var anyZero = false;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].CapturedVersion == 0)
            {
                anyZero = true;
                break;
            }
        }

        if (!anyZero) return null;

        var entry = entries[0];
        return new DcbBoundaryRead(entry.Query, tenantId, entry.LastSeenSequence, eventCount, eventTypeNames);
    }
}

/// <summary>
/// Storage operation that enforces the DCB tag boundary against the
/// <c>mt_dcb_tag_version</c> rows captured at fetch time, and applies the producer-side
/// bumps of the tags the save's own events carry. Replaces the racy SELECT-EXISTS over
/// <c>mt_events</c> that <see cref="AssertDcbConsistency"/> emitted. Each statement is the
/// serialization point: at READ COMMITTED, the row-level lock + <c>version = $captured</c>
/// predicate converts what was a predicate read into a row-level write conflict. Fixes #4591.
/// </summary>
/// <remarks>
/// #5887: the check LOCKS and COMPARES a row it read; it does not bump it. Only a tag carried
/// by an appended event moves a version, so a boundary conflicts with a later save exactly when
/// that save appended an event carrying a tag the boundary read -- the DCB append condition.
/// Bumping every row it read made two boundaries over one tag value conflict even when neither
/// appended anything the other reads. A no-op update still takes the row lock, so
/// concurrent boundary saves over one row still serialize: the second waits, then compares
/// against the committed row.
///
/// The rows checked and the rows bumped are one sorted set, locked in (tag_table, tag_value)
/// order in one pass. Sorting each half on its own is not enough: a save that checks row c and
/// bumps row w can then deadlock against a save that checks w and bumps c.
///
/// #5280: one save carries ONE of these, covering every boundary the session read. A row named
/// by two boundaries — the same query fetched twice, or two overlapping queries — appears once.
///
/// #6035: every value a bump gives a row comes from <c>mt_dcb_tag_version_sequence</c>, so a row never
/// holds the same value twice, even a row a tombstone deleted and an append created again. The one other
/// value a row can hold is 0, written by a check that found no row: 0 is what a missing row captures as,
/// and both mean nothing carrying the tag was appended since. So only a save that saw no row may create
/// one; a capture of any other value that finds the row gone has lost.
/// </remarks>
internal class DcbTagVersionAssertion: IStorageOperation
{
    /// <summary>
    /// #6035: asserted when one session read a row twice and saw it move. Neither read can still be valid,
    /// no row holds this value, and the entry cannot create a row either: it loses outright.
    /// </summary>
    internal const long NeverMatches = -1;

    private readonly EventGraph _events;
    private readonly IReadOnlyList<Row> _orderedRows;
    private readonly IReadOnlyList<DcbBoundaryRead> _reads;

    private readonly record struct Row(string TagTable, string TagValue, DcbTagVersionEntry? Captured, bool Bump);

    public DcbTagVersionAssertion(
        EventGraph events,
        IReadOnlyList<DcbTagVersionEntry> capturedEntries,
        IReadOnlyList<(string TagTable, string TagValue)> bumps,
        IReadOnlyList<DcbBoundaryRead>? reads = null)
    {
        _events = events;
        _reads = reads ?? [];

        var rows = new Dictionary<(string, string), Row>(capturedEntries.Count + bumps.Count);
        for (var i = 0; i < capturedEntries.Count; i++)
        {
            var entry = capturedEntries[i];
            var key = (entry.TagTable, entry.TagValue);
            if (!rows.TryGetValue(key, out var existing))
            {
                rows[key] = new Row(entry.TagTable, entry.TagValue, entry, false);
                continue;
            }

            // #6035: two reads that saw different values mean the row moved between them, so the older read
            // is already stale, and every read the session made has to still be valid. Asserting the lower
            // value used to do that, until a tombstone could delete a row: a read of 7 and a later read of "no
            // row" would have asserted the 0, created the row and won.
            if (existing.Captured!.Value.CapturedVersion != entry.CapturedVersion)
            {
                rows[key] = existing with
                {
                    Captured = existing.Captured.Value with { CapturedVersion = NeverMatches }
                };
            }
        }

        foreach (var (tagTable, tagValue) in bumps)
        {
            var key = (tagTable, tagValue);
            rows[key] = rows.TryGetValue(key, out var existing)
                ? existing with { Bump = true }
                : new Row(tagTable, tagValue, null, true);
        }

        // One deterministic order over everything the save locks -- this is what keeps two
        // concurrent saves touching overlapping rows from deadlocking. ConfigureCommand and
        // PostprocessAsync both iterate it.
        var ordered = new Row[rows.Count];
        rows.Values.CopyTo(ordered, 0);
        Array.Sort(ordered, static (a, b) => compareKeys(a.TagTable, a.TagValue, b.TagTable, b.TagValue));
        _orderedRows = ordered;
    }

    private static int compareKeys(string tableA, string valueA, string tableB, string valueB)
    {
        var byTable = string.CompareOrdinal(tableA, tableB);
        return byTable != 0 ? byTable : string.CompareOrdinal(valueA, valueB);
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var table = $"{_events.DatabaseSchemaName}.{DcbTagVersionTable.TableName}";
        var nextVersion = DcbTagVersionTable.NextVersionSql(_events);
        var tenantId = session.TenantId;

        for (var i = 0; i < _orderedRows.Count; i++)
        {
            // StartNewCommand (not `; <sql>`) so Npgsql sends each statement
            // as a separate batched command — multiple `;`-separated
            // statements in a single prepared statement raise Postgres
            // SQLSTATE 42601.
            if (i > 0) builder.StartNewCommand();

            var row = _orderedRows[i];
            var captured = row.Captured?.CapturedVersion;

            if (captured is { } version and not 0)
            {
                // #6035: a boundary that saw a row. Only that row, still at that value, will do: a missing row
                // was deleted by a tombstone since, and creating it again would match a stale read. The
                // UPDATE takes the row lock even when it leaves the version alone, waits out a concurrent
                // holder, and evaluates the WHERE against the committed row.
                builder.Append("update ");
                builder.Append(table);
                builder.Append(row.Bump ? $" set version = {nextVersion}" : " set version = version");
                builder.Append(" where tag_table = ");
                builder.AppendParameter(row.TagTable);
                builder.Append(" and tag_value = ");
                builder.AppendParameter(row.TagValue);
                builder.Append(" and tenant_id = ");
                builder.AppendParameter(tenantId);
                builder.Append(" and version = ");
                builder.AppendParameter(version);
                builder.Append(" returning 1");
                continue;
            }

            // A bump, or a boundary that saw no row (captured 0). INSERT … ON CONFLICT DO UPDATE handles both:
            //   - row missing → INSERT succeeds. Concurrent first-time creators serialize on the
            //     unique-PK insert, and the loser then takes the ON CONFLICT branch.
            //   - row exists → ON CONFLICT branch. It locks the row even when the update changes
            //     nothing, waits out a concurrent holder, and evaluates the WHERE against the
            //     committed row -- so a save that observed "no row" and finds one returns no row.
            //
            // A check that creates the row creates it at 0, not from the sequence, so a second check-only
            // boundary over the same key, which also captured 0, still matches it: only an appended event
            // moves a version.
            builder.Append("insert into ");
            builder.Append(table);
            builder.Append(" (tag_table, tag_value, tenant_id, version) values (");
            builder.AppendParameter(row.TagTable);
            builder.Append(", ");
            builder.AppendParameter(row.TagValue);
            builder.Append(", ");
            builder.AppendParameter(tenantId);
            builder.Append(row.Bump ? $", {nextVersion}" : ", 0");

            // ON CONFLICT DO UPDATE references the existing row via the
            // unqualified table name (`mt_dcb_tag_version.version`) — Postgres
            // does not accept the schema prefix in this clause. `excluded.version` is the value the insert
            // drew, so a bump draws one value either way.
            builder.Append(row.Bump
                ? ") on conflict (tag_table, tag_value, tenant_id) do update set version = excluded.version"
                : ") on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version");

            if (row.Captured.HasValue)
            {
                builder.Append(" where mt_dcb_tag_version.version = 0");
            }

            builder.Append(" returning 1");
        }

        foreach (var read in _reads)
        {
            builder.StartNewCommand();
            DcbBoundaryEventsSql.WriteCount(builder, _events, read);
        }
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        DcbTagVersionEntry? conflicted = null;

        for (var i = 0; i < _orderedRows.Count; i++)
        {
            if (i > 0)
            {
                // Advance past the previous statement's result set. The outer
                // OperationPage advances past the LAST result set on its own.
                await reader.NextResultAsync(token).ConfigureAwait(false);
            }

            var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);

            // A bump-only row has no WHERE and always returns its row. Keep iterating either way
            // so we consume the remaining result sets — the batch protocol requires every
            // statement's result set to be walked before the next operation can read its own.
            if (!hasRow && _orderedRows[i].Captured.HasValue)
            {
                // First loser wins the exception — the row carries the boundary
                // query that captured it, so the message names a query the caller
                // actually made even when the save spans several boundaries.
                conflicted ??= _orderedRows[i].Captured;
            }
        }

        DcbBoundaryRead? staleRead = null;
        foreach (var read in _reads)
        {
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var remaining = await reader.ReadAsync(token).ConfigureAwait(false)
                ? await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false)
                : 0;

            // Fewer, not different: the read can have kept fewer events than the query matches, if an
            // event of a type this application no longer knows was dropped as it was read.
            if (remaining < read.EventCount)
            {
                staleRead ??= read;
            }
        }

        if (conflicted.HasValue)
        {
            exceptions.Add(new DcbConcurrencyException(conflicted.Value.Query, conflicted.Value.LastSeenSequence));
        }
        else if (staleRead != null)
        {
            exceptions.Add(new DcbConcurrencyException(staleRead.Query, staleRead.LastSeenSequence));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
