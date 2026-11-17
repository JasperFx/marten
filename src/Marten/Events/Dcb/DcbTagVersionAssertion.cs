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
/// appended anything the other reads. A no-op <c>DO UPDATE</c> still takes the row lock, so
/// concurrent boundary saves over one row still serialize: the second waits, then compares
/// against the committed row.
///
/// The rows checked and the rows bumped are one sorted set, locked in (tag_table, tag_value)
/// order in one pass. Sorting each half on its own is not enough: a save that checks row c and
/// bumps row w can then deadlock against a save that checks w and bumps c.
///
/// #5280: one save carries ONE of these, covering every boundary the session read. A row named
/// by two boundaries — the same query fetched twice, or two overlapping queries — appears once,
/// held to the oldest version the session captured for it.
/// </remarks>
internal class DcbTagVersionAssertion: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly IReadOnlyList<Row> _orderedRows;

    private readonly record struct Row(string TagTable, string TagValue, DcbTagVersionEntry? Captured, bool Bump);

    public DcbTagVersionAssertion(
        EventGraph events,
        IReadOnlyList<DcbTagVersionEntry> capturedEntries,
        IReadOnlyList<(string TagTable, string TagValue)> bumps)
    {
        _events = events;

        // Oldest capture first within a (tag_table, tag_value) run, so collapsing the run keeps the
        // strictest check of the row: every read the session made has to still be valid, not just
        // the most recent one.
        var captured = new DcbTagVersionEntry[capturedEntries.Count];
        for (var i = 0; i < capturedEntries.Count; i++) captured[i] = capturedEntries[i];
        Array.Sort(captured, static (a, b) =>
        {
            var byKey = compareKeys(a.TagTable, a.TagValue, b.TagTable, b.TagValue);
            return byKey != 0 ? byKey : a.CapturedVersion.CompareTo(b.CapturedVersion);
        });

        var rows = new Dictionary<(string, string), Row>(captured.Length + bumps.Count);
        foreach (var entry in captured)
        {
            rows.TryAdd((entry.TagTable, entry.TagValue), new Row(entry.TagTable, entry.TagValue, entry, false));
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
        var schema = _events.DatabaseSchemaName;
        var tenantId = session.TenantId;

        for (var i = 0; i < _orderedRows.Count; i++)
        {
            // StartNewCommand (not `; <sql>`) so Npgsql sends each statement
            // as a separate batched command — multiple `;`-separated
            // statements in a single prepared statement raise Postgres
            // SQLSTATE 42601.
            if (i > 0) builder.StartNewCommand();

            var row = _orderedRows[i];

            // What the row holds once this save commits, and what a missing row is created with.
            // Version 0 means "no event carrying this tag has been appended", whether or not the
            // row exists, so a check that creates the row creates it at the version it captured.
            var captured = row.Captured?.CapturedVersion ?? 0;
            var increment = row.Bump ? 1 : 0;

            // INSERT … ON CONFLICT DO UPDATE WHERE handles both cases:
            //   - row missing → INSERT succeeds. Concurrent first-time creators serialize on the
            //     unique-PK insert, and the loser then takes the ON CONFLICT branch.
            //   - row exists → ON CONFLICT branch. It locks the row even when the update changes
            //     nothing, waits out a concurrent holder, and evaluates the WHERE against the
            //     committed row -- so a save that observed a stale version returns no row.
            builder.Append("insert into ");
            builder.Append(schema);
            builder.Append(".mt_dcb_tag_version (tag_table, tag_value, tenant_id, version) values (");
            builder.AppendParameter(row.TagTable);
            builder.Append(", ");
            builder.AppendParameter(row.TagValue);
            builder.Append(", ");
            builder.AppendParameter(tenantId);
            builder.Append(", ");
            builder.AppendParameter(captured + increment);
            // ON CONFLICT DO UPDATE references the existing row via the
            // unqualified table name (`mt_dcb_tag_version.version`) — Postgres
            // does not accept the schema prefix in this clause.
            builder.Append(row.Bump
                ? ") on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version + 1"
                : ") on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version");

            if (row.Captured.HasValue)
            {
                builder.Append(" where mt_dcb_tag_version.version = ");
                builder.AppendParameter(captured);
            }

            builder.Append(" returning 1");
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

        if (conflicted.HasValue)
        {
            exceptions.Add(new DcbConcurrencyException(conflicted.Value.Query, conflicted.Value.LastSeenSequence));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
