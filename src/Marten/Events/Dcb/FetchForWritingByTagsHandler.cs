#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

internal class FetchForWritingByTagsHandler<T>: IQueryHandler<IEventBoundary<T>> where T : class
{
    private readonly DocumentStore _store;
    private readonly EventTagQuery _query;

    // Distinct (TagTable, TagValue) targets for the side-table UPSERT + capture
    // step appended at the tail of ConfigureCommand. Populated there, replayed
    // in HandleAsync so the captured versions land in the same slots the
    // assertion expects. See #4591.
    private List<(string TagTable, string TagValue)>? _versionTargets;

    public FetchForWritingByTagsHandler(DocumentStore store, EventTagQuery query)
    {
        _store = store;
        _query = query;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var storage = (EventDocumentStorage)((DocumentSessionBase)session).EventStorage();
        var selectFields = storage.SelectFields();
        var conditions = _query.Conditions;
        var schema = _store.Events.DatabaseSchemaName;
        var isHStore = _store.Events.DcbStorageMode == DcbStorageMode.HStore;

        // #5300: the version capture goes FIRST, ahead of the events SELECT.
        // The two are separate statements, and at READ COMMITTED each statement
        // takes its own snapshot -- so a save that commits while one of them is
        // running is visible to the other but not to it. Capturing after the
        // events read pairs a FRESH version with a STALE aggregate, and the
        // save's `where version = $captured` then matches: the conflict is
        // missed and both appends commit. Capturing first can only pair a STALE
        // version with a fresh aggregate, which fails the assertion -- a
        // spurious conflict the caller retries, never a lost one. Same ordering
        // (and same reason) as FetchAsyncPlan.FetchForWriting, which reads the
        // stream version ahead of the events.
        AppendVersionCapture(builder, (IMartenSession)session);
        builder.StartNewCommand();

        builder.Append("select ");
        for (var f = 0; f < selectFields.Length; f++)
        {
            if (f > 0) builder.Append(", ");
            builder.Append("e.");
            builder.Append(selectFields[f]);
        }

        builder.Append(" from ");
        builder.Append(schema);
        builder.Append(".mt_events e");

        if (!isHStore)
        {
            var distinctTagTypes = conditions.Select(c => c.TagType).Distinct().ToList();
            for (var i = 0; i < distinctTagTypes.Count; i++)
            {
                var tagType = distinctTagTypes[i];
                var registration = _store.Events.FindTagType(tagType)
                                   ?? throw new InvalidOperationException(
                                       $"Tag type '{tagType.Name}' is not registered.");

                builder.Append(" left join ");
                builder.Append(schema);
                builder.Append(".mt_event_tag_");
                builder.Append(registration.TableSuffix);
                builder.Append(" t");
                builder.Append(i.ToString());
                builder.Append(" on e.seq_id = t");
                builder.Append(i.ToString());
                builder.Append(".seq_id");
            }

            builder.Append(" where (");
            for (var i = 0; i < conditions.Count; i++)
            {
                if (i > 0) builder.Append(" or ");

                var condition = conditions[i];
                var tagIndex = distinctTagTypes.IndexOf(condition.TagType);

                builder.Append("(t");
                builder.Append(tagIndex.ToString());
                builder.Append(".value = ");

                var registration = _store.Events.FindTagType(condition.TagType)!;
                var value = registration.ExtractValue(condition.TagValue);
                builder.AppendParameter(value);

                if (condition.EventType != null)
                {
                    builder.Append(" and e.type = ");
                    var eventTypeName = _store.Events.EventMappingFor(condition.EventType).EventTypeName;
                    builder.AppendParameter(eventTypeName);
                }

                builder.Append(")");
            }

            builder.Append(")");
        }
        else
        {
            builder.Append(" where ");
            HStoreDcbQueryFragment.AppendOrPredicate(builder, _store.Events, conditions, "e");
        }

        // Filter by tenant_id for conjoined tenancy
        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and e.tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        // If the aggregator has event type filtering, apply it to limit the returned events
        var eventTypeNames = resolveAggregatorEventTypeNames();
        if (eventTypeNames != null)
        {
            builder.Append(" and e.type = ANY(");
            var parameter = builder.AppendParameter(eventTypeNames);
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar | NpgsqlDbType.Array;
            builder.Append(")");
        }

        builder.Append(" order by e.seq_id");
    }

    private void AppendVersionCapture(ICommandBuilder builder, IMartenSession session)
    {
        var conditions = _query.Conditions;
        var schema = _store.Events.DatabaseSchemaName;

        // Distinct by (TagTable, TagValue) — the side table is keyed by table
        // suffix + canonical string value (see TagValueStringifier), not by the
        // condition's optional EventType filter.
        var targets = new List<(string TagTable, string TagValue)>(conditions.Count);
        var seen = new HashSet<(string, string)>();
        foreach (var condition in conditions)
        {
            var registration = _store.Events.FindTagType(condition.TagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{condition.TagType.Name}' is not registered.");
            var raw = registration.ExtractValue(condition.TagValue);
            var tagValue = TagValueStringifier.Stringify(raw);
            var key = (registration.TableSuffix, tagValue);
            if (seen.Add(key))
            {
                targets.Add((registration.TableSuffix, tagValue));
            }
        }

        _versionTargets = targets;

        var tenantId = session.TenantId;

        // No StartNewCommand here -- this is the handler's FIRST statement, and the
        // batched-query path (CommandExtensions.BuildCommand) already opens a command
        // per handler. A second call back to back would push an empty statement into
        // the batch and shift every NextResultAsync in HandleAsync by one. The
        // boundary between this statement and the events SELECT is opened by the
        // caller instead.

        // Plain SELECT — no INSERT here. The fetch query shares the session
        // connection, and any INSERT-OR-IGNORE here would hold the row lock
        // until SaveChangesAsync, deadlocking concurrent fetchers that all
        // target the same boundary row. Row creation is deferred to
        // DcbTagVersionAssertion's INSERT … ON CONFLICT DO UPDATE WHERE, which
        // runs inside the save transaction where holding the lock is correct.
        //
        // Missing rows return no row from this SELECT; HandleAsync substitutes
        // captured_version = 0, and the save's INSERT path takes care of the
        // first-time create (with concurrent first-time creators racing on the
        // unique-PK constraint, exactly as desired).
        builder.Append("select tag_table, tag_value, version from ");
        builder.Append(schema);
        builder.Append(".mt_dcb_tag_version where tenant_id = ");
        var tenantParam = builder.AppendParameter(tenantId);
        tenantParam.NpgsqlDbType = NpgsqlDbType.Varchar;
        builder.Append(" and (tag_table, tag_value) in (");
        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0) builder.Append(", ");
            builder.Append("(");
            var tableParam = builder.AppendParameter(targets[i].TagTable);
            tableParam.NpgsqlDbType = NpgsqlDbType.Varchar;
            builder.Append(", ");
            var valueParam = builder.AppendParameter(targets[i].TagValue);
            valueParam.NpgsqlDbType = NpgsqlDbType.Text;
            builder.Append(")");
        }
        builder.Append(")");
    }

    private string[]? resolveAggregatorEventTypeNames()
    {
        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        if (aggregator is not EventFilterable filterable) return null;

        var includedTypes = filterable.IncludedEventTypes;
        if (includedTypes.Count == 0 || includedTypes.Any(x => x.IsAbstract || x.IsInterface)) return null;

        var additionalAliases = _store.Events.AliasesForEvents(includedTypes);
        return includedTypes
            .Select(x => _store.Events.EventMappingFor(x).Alias)
            .Union(additionalAliases)
            .ToArray();
    }

    public async Task<IEventBoundary<T>> HandleAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token)
    {
        var docSession = (DocumentSessionBase)session;
        var storage = (EventDocumentStorage)docSession.EventStorage();

        // #4591/#5300: FIRST result set is the per-tag captured versions for the side-table
        // assertion. Always present because AppendVersionCapture always runs (any DCB tag
        // query necessarily references at least one tag). It has to be read before the
        // events -- see the ordering note in ConfigureCommand.
        var capturedByKey = await readCapturedVersions(reader, token).ConfigureAwait(false);

        await reader.NextResultAsync(token).ConfigureAwait(false);

        var events = new List<IEvent>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var @event = await storage.ResolveAsync(reader, token).ConfigureAwait(false);
            events.Add(@event);
        }

        var lastSeenSequence = events.Count > 0 ? events.Max(e => e.Sequence) : 0;

        var capturedVersions = buildEntries(capturedByKey, lastSeenSequence);

        T? aggregate = default;
        if (events.Count > 0)
        {
            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            if (aggregator == null)
            {
                throw new InvalidOperationException(
                    $"Cannot find an aggregator for type '{typeof(T).Name}'.");
            }

            aggregate = await aggregator.BuildAsync(events, docSession, default, token).ConfigureAwait(false);
        }

        // #5276/#5280: the captured rows are handed to the session, not turned into an operation
        // here. The boundary condition guards an append, so it only belongs in the unit of work if
        // this session appends something -- and every boundary the session reads folds into a single
        // assertion so one row is never asserted twice. See
        // DocumentSessionBase.QueuePendingDcbBoundaryAssertions, which drains this at
        // ProcessEventsAsync time, ahead of the producer-side bumps.
        docSession.CaptureDcbBoundary(capturedVersions);

        return new EventBoundary<T>(docSession, _store.Events, aggregate, events, lastSeenSequence);
    }

    private static async Task<Dictionary<(string, string), long>> readCapturedVersions(
        DbDataReader reader, CancellationToken token)
    {
        // SELECT returns only the rows that exist. A missing row means no prior
        // save has touched this tag boundary yet — captured version = 0, and the
        // assertion's INSERT … ON CONFLICT will create the row at save time
        // (with concurrent first-time creators racing on the unique-PK insert).
        var byKey = new Dictionary<(string, string), long>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var tagTable = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var tagValue = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false);
            byKey[(tagTable, tagValue)] = version;
        }

        return byKey;
    }

    private IReadOnlyList<DcbTagVersionEntry> buildEntries(
        Dictionary<(string, string), long> byKey, long lastSeenSequence)
    {
        var targets = _versionTargets!;
        var entries = new DcbTagVersionEntry[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            var key = (targets[i].TagTable, targets[i].TagValue);
            byKey.TryGetValue(key, out var version);   // missing → version = 0

            // #5280: each row carries the query that captured it, so a merged assertion covering
            // several boundaries still reports the one that actually lost.
            entries[i] = new DcbTagVersionEntry(targets[i].TagTable, targets[i].TagValue, version,
                _query, lastSeenSequence);
        }

        return entries;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
