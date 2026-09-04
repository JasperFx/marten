#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;
using Marten.Events.Querying;
using Marten.Linq.MatchesSql;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Storage;

namespace Marten.Events;

internal class QueryEventStore: IQueryEventStore, IReadOnlyEventStore
{
    private readonly QuerySession _session;
    private readonly DocumentStore _store;
    protected readonly Tenant _tenant;

    public QueryEventStore(QuerySession session, DocumentStore store, Tenant tenant)
    {
        _session = session;
        _store = store;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
    {
        var selector = _store.Events.EnsureAsGuidStorage(_session);

        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new EventStatement(selector, _store.Events)
        {
            StreamId = streamId,
            Version = version,
            Timestamp = timestamp,
            TenantId = _tenant.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
    {
        var selector = _store.Events.EnsureAsStringStorage(_session);

        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new EventStatement(selector, _store.Events)
        {
            StreamKey = streamKey,
            Version = version,
            Timestamp = timestamp,
            TenantId = _tenant.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<T?> AggregateStreamAsync<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamId, version, timestamp, fromVersion, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return state;
        }

        if (version != 0 && version > events[events.Count - 1].Version) return null;

        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        if (aggregate == null)
        {
            return null;
        }

        if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
        {
            storage.SetIdentityFromGuid(aggregate, streamId);
        }

        return aggregate;
    }

    public async Task<T?> AggregateStreamToLastKnownAsync<T>(Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamId, version, timestamp, 0, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        T? aggregate = null;
        while (aggregate == null && events.Any())
        {
            aggregate = await aggregator.BuildAsync(events, _session, default, token).ConfigureAwait(false);
            events = events.SkipLast(1).ToList();
        }

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage!.SetIdentityFromGuid(aggregate, streamId);
            }
        }

        return aggregate;
    }


    public async Task<T?> AggregateStreamAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamKey, version, timestamp, fromVersion, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return state;
        }

        if (version != 0 && version > events[events.Count - 1].Version) return null;

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage.SetIdentityFromString(aggregate, streamKey);
            }
        }

        return aggregate;
    }

    public async Task<T?> AggregateStreamToLastKnownAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamKey, version, timestamp, 0, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        T? aggregate = null;
        while (aggregate == null && events.Any())
        {
            aggregate = await aggregator.BuildAsync(events, _session, default, token).ConfigureAwait(false);
            events = events.SkipLast(1).ToList();
        }

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage!.SetIdentityFromString(aggregate, streamKey);
            }
        }

        return aggregate;
    }

    public IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : notnull
    {
        _store.Events.AddEventType<T>();

        return _session.Query<T>();
    }

    public IMartenQueryable<IEvent> QueryAllRawEvents()
    {
        return _session.Query<IEvent>();
    }

    public async Task<IEvent<T>?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

        _store.Events.AddEventType<T>();

        return (await LoadAsync(id, token).ConfigureAwait(false))?.As<Event<T>>();
    }

    public async Task<IEvent?> LoadAsync(Guid id, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

        var handler = new SingleEventQueryHandler(id, _session.EventStorage());
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<StreamState?> FetchStreamStateAsync(Guid streamId, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);
        var handler = eventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant.TenantId));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<StreamState?> FetchStreamStateAsync(string streamKey, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);
        var handler = eventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant.TenantId));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    private IEventStorage eventStorage()
    {
        return _store.Options.Providers.StorageFor<IEvent>().QueryOnly.As<IEventStorage>();
    }

    // IReadOnlyEventStore explicit implementations
    async Task<JasperFx.Events.StreamState?> IReadOnlyEventStore.FetchStreamStateAsync(Guid streamId, CancellationToken token)
    {
        var state = await FetchStreamStateAsync(streamId, token).ConfigureAwait(false);
        return state != null ? ToJasperFxStreamState(state) : null;
    }

    async Task<JasperFx.Events.StreamState?> IReadOnlyEventStore.FetchStreamStateAsync(string streamKey, CancellationToken token)
    {
        var state = await FetchStreamStateAsync(streamKey, token).ConfigureAwait(false);
        return state != null ? ToJasperFxStreamState(state) : null;
    }

    public async Task<PagedEvents> QueryEventsAsync(EventQuery query, CancellationToken token = default)
    {
        // jasperfx#737 guard rail, first thing: declare exactly what this store honors, so a query
        // carrying anything else is refused with a NotSupportedException naming the field rather
        // than silently ignored (unfiltered results would read as filtered).
        //
        // Marten honors every EventQuery filter, with one honesty carve-out: the metadata filters
        // (correlation_id, causation_id, user_name) are only queryable when the store actually
        // captures the corresponding column — the LINQ member registration in EventQueryMapping is
        // itself gated by the Metadata.X.Enabled flag (see EventQueryMapping.cs), so a Where on a
        // disabled member would fail to translate. Pre-#737 this method silently skipped those
        // filters; now the disabled column is subtracted from the declared set so the assert
        // refuses the filter by name instead.
        var eventMetadata = _store.Events.Metadata;
        var supportedFilters = EventQueryFilters.All;
        if (!eventMetadata.CorrelationId.Enabled)
        {
            supportedFilters &= ~EventQueryFilters.CorrelationId;
        }

        if (!eventMetadata.CausationId.Enabled)
        {
            supportedFilters &= ~EventQueryFilters.CausationId;
        }

        if (!eventMetadata.UserName.Enabled)
        {
            supportedFilters &= ~EventQueryFilters.UserName;
        }

        query.AssertFiltersAreSupported(supportedFilters);

        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var queryable = QueryAllRawEvents();

        // #5021 / jasperfx#555 — honour the tenant scope the Event Explorer sets. On a conjoined
        // multi-tenant store the same event can exist under two tenants, so the Explorer sets
        // EventQuery.TenantId to isolate one tenant; TenantIsOneOf overrides the session's own tenant
        // filter (the Explorer reads through an AllowAnyTenant session). A null TenantId is left
        // untouched: the query keeps the session's existing tenancy — the store-global read on an
        // AllowAnyTenant session, or the session's own tenant on a tenant-scoped session. This preserves
        // the pre-existing QueryEventsAsync contract (TenantPartitionedEventsTests
        // QueryEventsAsync_pages_only_within_the_querying_tenant) exactly.
        if (query.TenantId != null)
        {
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.TenantIsOneOf(query.TenantId));
        }

        // jasperfx#737: EventTypeName and EventTypeNames are one filter with two spellings — the
        // union semantics live upstream in CombinedEventTypeNames() so all three stores agree.
        var eventTypeNames = query.CombinedEventTypeNames();
        if (eventTypeNames.Count == 1)
        {
            var single = eventTypeNames[0];
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.EventTypeName == single);
        }
        else if (eventTypeNames.Count > 1)
        {
            var names = eventTypeNames.ToArray();
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.EventTypeName.IsOneOf(names));
        }

        if (query.StreamId != null)
        {
            if (Guid.TryParse(query.StreamId, out var streamGuid))
            {
                queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.StreamId == streamGuid);
            }
            else
            {
                queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.StreamKey == query.StreamId);
            }
        }

        // #4791 / CritterWatch #629: exact-match filters on the event's metadata columns. The
        // AssertFiltersAreSupported call above already refused any of these whose column is not
        // captured, so reaching here means the member is registered and translates.
        if (query.CorrelationId != null)
        {
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.CorrelationId == query.CorrelationId);
        }

        if (query.CausationId != null)
        {
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.CausationId == query.CausationId);
        }

        if (query.UserName != null)
        {
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.UserName == query.UserName);
        }

        // jasperfx#737: inclusive timestamp window on the server-assigned timestamp. An inverted
        // window needs no special case — the AND of the two bounds already matches nothing.
        if (query.TimestampFrom != null)
        {
            var from = query.TimestampFrom.Value;
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.Timestamp >= from);
        }

        if (query.TimestampTo != null)
        {
            var to = query.TimestampTo.Value;
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.Timestamp <= to);
        }

        // jasperfx#737: inclusive sequence window on the store-global sequence, same shape.
        if (query.SequenceFloor != null)
        {
            var floor = query.SequenceFloor.Value;
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.Sequence >= floor);
        }

        if (query.SequenceCeiling != null)
        {
            var ceiling = query.SequenceCeiling.Value;
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.Sequence <= ceiling);
        }

        // jasperfx#737: the folded DCB tag conditions. The spec's OR'd conditions select events,
        // and that selection ANDs into everything above via a raw SQL fragment that mirrors the
        // translation HasTagParser / BuildTagQuerySql use for the same storage modes.
        if (query.TagConditions != null)
        {
            var (tagSql, tagParameters) = buildTagConditionsFilter(query.TagConditions);
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.MatchesSql(tagSql, tagParameters));
        }

        var totalCount = await queryable.CountAsync(token).ConfigureAwait(false);

        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var offset = (pageNumber - 1) * query.PageSize;

        // Sequence-ascending is contract (see IReadOnlyEventStore.QueryEventsAsync), and the paging
        // walks that ordering.
        var events = await queryable
            .OrderBy(e => e.Sequence)
            .Skip(offset)
            .Take(query.PageSize)
            .ToListAsync(token)
            .ConfigureAwait(false);

        return new PagedEvents
        {
            Events = events,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = query.PageSize
        };
    }

    /// <summary>
    /// Translate the wire-form <see cref="EventTagQuerySpec"/> into a raw SQL fragment over the
    /// event query's <c>d</c> alias (mt_events), OR'ing the conditions. Each condition takes the
    /// same per-storage-mode shape as <c>HasTagParser</c>: a correlated tag-table subquery in
    /// TagTables mode, hstore containment in HStore mode, with an optional <c>d.type</c> predicate
    /// when the condition is scoped to an event type. IN-subqueries / containment are set
    /// semantics, so an event matching several conditions still appears once.
    /// </summary>
    private (string sql, object[] parameters) buildTagConditionsFilter(EventTagQuerySpec spec)
    {
        var events = _store.Events;

        // Resolve the wire descriptors back to CLR types against the registered tag/event graph.
        var knownTypes = events.TagTypes.Select(x => x.TagType)
            .Concat(events.AllKnownEventTypes().Select(x => x.EventType));
        var tagQuery = spec.Resolve(EventTagQuerySpec.ResolverFor(knownTypes));

        var conditions = tagQuery.Conditions;
        if (conditions.Count == 0)
        {
            throw new ArgumentException("EventQuery.TagConditions must carry at least one condition.");
        }

        var schema = events.DatabaseSchemaName;
        var isHStore = events.DcbStorageMode == DcbStorageMode.HStore;
        var isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;

        var sb = new System.Text.StringBuilder();
        var parameters = new List<object>();

        sb.Append('(');
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" or ");
            }

            var condition = conditions[i];
            var registration = events.FindTagType(condition.TagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{condition.TagType.Name}' is not registered. Call RegisterTagType<{condition.TagType.Name}>() first.");

            sb.Append('(');
            if (isHStore)
            {
                sb.Append("d.tags @> hstore(?, ?)");
                parameters.Add(registration.TableSuffix);
                parameters.Add(TagValueStringifier.Stringify(registration.ExtractValue(condition.TagValue)));
            }
            else
            {
                // Under conjoined tenancy seq_id is not unique across tenants (per-tenant
                // sequences), so the correlated subquery must also match tenant_id; the outer
                // event query is already tenant-scoped. Mirrors HasTagParser / #4645.
                sb.Append("d.seq_id in (select seq_id from ");
                sb.Append(schema);
                sb.Append(".mt_event_tag_");
                sb.Append(registration.TableSuffix);
                sb.Append(" where value = ?");
                if (isConjoined)
                {
                    sb.Append(" and tenant_id = d.tenant_id");
                }

                sb.Append(')');
                parameters.Add(registration.ExtractValue(condition.TagValue));
            }

            if (condition.EventType != null)
            {
                sb.Append(" and d.type = ?");
                parameters.Add(events.EventMappingFor(condition.EventType).EventTypeName);
            }

            sb.Append(')');
        }

        sb.Append(')');

        return (sb.ToString(), parameters.ToArray());
    }

    private static JasperFx.Events.StreamState ToJasperFxStreamState(StreamState martenState)
    {
        return new JasperFx.Events.StreamState
        {
            Id = martenState.Id,
            Key = martenState.Key,
            Version = martenState.Version,
            AggregateType = martenState.AggregateType,
            LastTimestamp = martenState.LastTimestamp,
            Created = martenState.Created,
            IsArchived = martenState.IsArchived,
            CompactedVersion = martenState.CompactedVersion
        };
    }

    /// <summary>
    /// jasperfx#740 (marten#5333): the streams table as a real <see cref="IQueryable{T}"/> of
    /// <see cref="StreamState"/>, translated against <c>mt_streams</c> and executed through the
    /// shared <see cref="JasperFx.Events.Documents.IDocumentQueryExecutor"/> hook. See
    /// <see cref="StreamStateQueryProvider"/> for the translatable set and the refusal rules.
    /// </summary>
    public IQueryable<StreamState> QueryStreamStates(string? tenantId = null)
    {
        // A tenant scope against a store with no tenant dimension must be refused, never quietly
        // answered with unscoped rows that read as tenant-scoped — the jasperfx#737 rule applied
        // to the tenant filter.
        if (tenantId != null && _store.Events.TenancyStyle != TenancyStyle.Conjoined)
        {
            throw new NotSupportedException(
                $"This event store does not have a tenant dimension (TenancyStyle.{_store.Events.TenancyStyle}), so QueryStreamStates cannot scope to tenant '{tenantId}'. Omit the tenantId argument, or configure conjoined multi-tenancy.");
        }

        return new StreamStateQueryProvider(_session, _store, _tenant, tenantId).CreateRoot();
    }
}
