using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using JasperFx.Events.Protected;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.OpenTelemetry;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Subscriptions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Postgresql.SqlGeneration;
using EventTypeFilter = Marten.Events.Daemon.Internals.EventTypeFilter;
using System.Diagnostics.CodeAnalysis;

namespace Marten;

[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class DocumentStore: IEventStore<IDocumentOperations, IQuerySession>, ISubscriptionRunner<ISubscription>
{
    static DocumentStore()
    {
        ProjectionExceptions.RegisterTransientExceptionType<NpgsqlException>();
        ProjectionExceptions.RegisterTransientExceptionType<MartenCommandException>();
    }

    DatabaseCardinality IEventStore.DatabaseCardinality => Options.Tenancy.Cardinality;

    bool IEventStore.HasMultipleTenants
    {
        get
        {
            if (Options.Tenancy.Cardinality != DatabaseCardinality.Single) return true;

            if (Options.Events.TenancyStyle == TenancyStyle.Conjoined) return true;

            // 9.0: AllDocumentMappings is lazy now (#4303); materialize
            // before scanning for a Conjoined document type.
            Options.Storage.BuildAllMappings();
            if (Options.Storage.AllDocumentMappings.Any(x => x.TenancyStyle == TenancyStyle.Conjoined)) return true;

            return false;
        }
    }

    // wolverine#3280: under sharded databases + per-tenant event partitioning, multiple tenants are
    // co-located in one shard database and each draws its own mt_events_sequence_<tenant> (independent,
    // overlapping). A single store-global high-water cannot track them, so node-distributed daemons must
    // fan out one agent per (shard, tenant). Single-database partitioning and database-per-tenant do NOT
    // need this (one agent per database already covers a single tenant or the single partitioned table).
    bool IEventStore.DistributesAgentsPerTenant
        => Options.Events.UseTenantPartitionedEvents && Options.Tenancy is Storage.ShardedTenancy;

    // JasperFx/marten#4806 (opt-in, default off): with per-tenant agents scattered across many shard
    // databases, group each database's agents onto one node so a node opens pools only to the databases it
    // owns (pools scale with databases, not nodes×databases). Only meaningful for the sharded per-tenant
    // store this fans agents out on.
    bool IEventStore.GroupAgentAssignmentsByDatabase
        => Options.Events.UseDatabaseAffineAgentAssignment
           && Options.Events.UseTenantPartitionedEvents
           && Options.Tenancy is Storage.ShardedTenancy;

    // JasperFx/marten#4806: bounded fan-out ("mix") — how many nodes a shard database's agents may spread
    // across when database-affine assignment is on. 1 = strict affinity. Clamped to >= 1.
    int IEventStore.MaxNodesPerDatabaseForAgents
        => Math.Max(1, Options.Events.DatabaseAffineAgentFanout);

    async ValueTask<IReadOnlyList<IEventDatabase>> IEventStore.AllDatabases()
    {
        // Straight delegation to ITenancy, mirroring IMartenStorage.AllDatabases(). The IMartenDatabase
        // interface does not itself extend IEventDatabase (only the concrete MartenDatabase does), so this
        // projects rather than returning the list directly. See #4570.
        var databases = await Tenancy.BuildDatabases().ConfigureAwait(false);
        return databases.OfType<IEventDatabase>().ToList();
    }

    public EventStoreIdentity Identity { get; }

    IEventRegistry IEventStore<IDocumentOperations, IQuerySession>.Registry => Options.EventGraph;

    public Type IdentityTypeForProjectedType(Type aggregateType)
    {
        return new DocumentMapping(aggregateType, Options).DocumentType;
    }

    string IEventStore<IDocumentOperations, IQuerySession>.DefaultDatabaseName =>
        Options.Tenancy.Default.Database.Identifier;

    ErrorHandlingOptions IEventStore<IDocumentOperations, IQuerySession>.ContinuousErrors =>
        Options.Projections.Errors;

    ErrorHandlingOptions IEventStore<IDocumentOperations, IQuerySession>.RebuildErrors =>
        Options.Projections.RebuildErrors;

    IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> IEventStore<IDocumentOperations, IQuerySession>.
        AllShards()
    {
        return Options.Projections.AllShards();
    }

    Meter IEventStore.Meter => Options.OpenTelemetry.Meter;

    ActivitySource IEventStore.ActivitySource => MartenTracing.ActivitySource;

    TimeProvider IEventStore<IDocumentOperations, IQuerySession>.TimeProvider => Options.Events.TimeProvider;

    string IEventStore.MetricsPrefix => "marten";

    AutoCreate IEventStore<IDocumentOperations, IQuerySession>.AutoCreateSchemaObjects =>
        Options.AutoCreateSchemaObjects;

    async Task IEventStore<IDocumentOperations, IQuerySession>.RewindSubscriptionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token,
        long? sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var names = Options
            .Projections
            .AllShards()
            .Where(x => x.Name.Name.EqualsIgnoreCase(subscriptionName) || x.Name.Identity == subscriptionName)
            .Select(x => x.Name)
            .ToArray();

        if (names.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName),
                $"Unknown subscription name '{subscriptionName}'. Available options are {Options.Projections.AllShards().Select(x => x.Name.Name).Distinct().Join(", ")}");
        }

        foreach (var name in names)
        {
            if (sequenceFloor.Value == 0)
            {
                session.QueueSqlCommand($"delete from {Options.EventGraph.ProgressionTable} where name = ?",
                    name.Identity);
            }
            else
            {
                session.QueueSqlCommand(
                    $"insert into {Options.EventGraph.ProgressionTable} (name, last_seq_id) values (?, ?) on conflict (name) do update set last_seq_id = ?",
                    name.Identity, sequenceFloor, sequenceFloor);
            }
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x =>
            x.ProjectionName == subscriptionName && x.EventSequence >= sequenceFloor);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStore<IDocumentOperations, IQuerySession>.RewindAgentProgressAsync(IEventDatabase database,
        string shardName, CancellationToken token, long sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        if (sequenceFloor > 0)
        {
            session.QueueSqlCommand(
                $"insert into {Options.EventGraph.ProgressionTable} (name, last_seq_id) values (?, ?) on conflict (name) do update set last_seq_id = ?",
                shardName, sequenceFloor, sequenceFloor);
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStore<IDocumentOperations, IQuerySession>.TeardownExistingProjectionStateAsync(IEventDatabase database, string subscriptionName, CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(subscriptionName));

        if (source == null)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName));
        }

        if (source is CompositeProjection composite)
        {
            foreach (var leafSource in composite.AllProjections())
            {
                teardownProjectionStorage(leafSource, session);
            }

            // Have to do the parent projection too!
            foreach (var agent in source.Shards())
            {
                session.QueueOperation(new DeleteProjectionProgress(Events, agent.Name.Identity));
            }
        }
        else
        {
            teardownProjectionStorage(source, session);
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStore<IDocumentOperations, IQuerySession>.DeleteProjectionProgressAsync(IEventDatabase database, string subscriptionName, CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(subscriptionName));

        if (source == null)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName));
        }

        foreach (var agent in source.Shards())
        {
            session.QueueOperation(new DeleteProjectionProgress(Events, agent.Name.Identity));
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// #4596 Phase 1 Session 4 + Phase 2c — override the jasperfx#407
    /// default-throwing per-tenant overload. Non-null <paramref name="tenantId"/>
    /// scopes the whole pre-rebuild reset to that one tenant:
    /// <list type="bullet">
    ///   <item><description>Per-tenant progression row(s) for every registered shard
    ///   (the <c>{ProjName}:{ShardKey}:{tenantId}</c> identities) — Session 4 work.</description></item>
    ///   <item><description><b>Per-tenant projected documents</b> for every leaf
    ///   (composite member or single source) of the projection — Phase 2c. Uses
    ///   <see cref="AsyncOptionsExtensions.TeardownForTenant"/> which emits
    ///   <c>DELETE FROM &lt;table&gt; WHERE tenant_id = '$tenant'</c> instead of
    ///   TRUNCATE, so other tenants' rows are untouched.</description></item>
    ///   <item><description>Tenant-scoped <see cref="DeadLetterEvent"/>s for the projection.</description></item>
    /// </list>
    /// JasperFx Phase 2b's <c>rebuildProjectionForTenant</c> explicitly does NOT call
    /// the store-global <c>TeardownExistingProjectionStateAsync</c> (that would wipe
    /// every other tenant's data) — this method is the only Marten-side hook fired
    /// before a per-tenant rebuild starts, so the tenant-scoped doc teardown rides
    /// along with the progression delete. Null <paramref name="tenantId"/> preserves
    /// today's "drop every shard progression row for this projection" semantics
    /// (no docs touched — the rebuild path uses TeardownExistingProjectionStateAsync
    /// for that).
    /// </summary>
    async Task IEventStore<IDocumentOperations, IQuerySession>.DeleteProjectionProgressAsync(
        IEventDatabase database, string subscriptionName, string? tenantId, CancellationToken token)
    {
        if (tenantId == null)
        {
            await ((IEventStore<IDocumentOperations, IQuerySession>)this)
                .DeleteProjectionProgressAsync(database, subscriptionName, token).ConfigureAwait(false);
            return;
        }

        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(subscriptionName));
        if (source == null)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName));
        }

        // Tenant-scoped doc teardown — same set of leaves the store-global
        // TeardownExistingProjectionStateAsync visits, but DELETE WHERE tenant_id
        // = $tenant instead of TRUNCATE.
        if (source is CompositeProjection composite)
        {
            foreach (var leafSource in composite.AllProjections())
            {
                teardownProjectionStorageForTenant(leafSource, session, tenantId);
            }
        }
        else
        {
            teardownProjectionStorageForTenant(source, session, tenantId);
        }

        foreach (var agent in source.Shards())
        {
            // Compose the per-tenant ShardName so its Identity carries the
            // trailing :tenantId — that's the row this DELETE targets.
            var tenantShardName = ShardName.Compose(agent.Name.Name, agent.Name.ShardKey, tenantId, agent.Name.Version);
            session.QueueOperation(new DeleteProjectionProgress(Events, tenantShardName.Identity));
        }

        // Note: DeadLetterEvent intentionally NOT wiped here. The store-global
        // teardown wipes them blanket-style, but jasperfx#407 Phase 2b's
        // per-tenant rebuild is single-tenant-scoped — the dead-letter table is
        // store-global and doesn't have a `tenant_id` column to scope on. Stale
        // entries from a previous failed apply will re-fire (or not) on the
        // rebuild and self-correct.

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private void teardownProjectionStorageForTenant(IProjectionSource<IDocumentOperations, IQuerySession> source,
        IDocumentSession session, string tenantId)
    {
        if (source.Options.TeardownDataOnRebuild)
        {
            source.Options.TeardownForTenant(session, tenantId);
        }
    }

    private void teardownProjectionStorage(IProjectionSource<IDocumentOperations, IQuerySession> source, IDocumentSession session)
    {
        if (source.Options.TeardownDataOnRebuild)
        {
            source.Options.Teardown(session);
        }

        foreach (var agent in source.Shards())
        {
            session.QueueOperation(new DeleteProjectionProgress(Events, agent.Name.Identity));
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.Name);

        // #4788: a [NaturalKey] aggregate maintains its mt_natural_key_X lookup table via the
        // auto-registered NaturalKeyProjection on the inline-append path. Teardown of the parent
        // projection must also wipe the natural-key table so the rebuild path repopulates it from
        // scratch (the rebuild itself re-emits the upserts via StartProjectionBatchAsync).
        if (source is IAggregateProjection aggregateSource && aggregateSource.NaturalKeyDefinition != null)
        {
            var naturalKeyTable =
                $"{Events.DatabaseSchemaName}.mt_natural_key_{aggregateSource.NaturalKeyDefinition.AggregateType.Name.ToLowerInvariant()}";
            session.QueueSqlCommand($"delete from {naturalKeyTable}");
        }
    }

    public async ValueTask<IProjectionBatch<IDocumentOperations, IQuerySession>> StartProjectionBatchAsync(
        EventRange range, IEventDatabase database, ShardExecutionMode mode,
        AsyncOptions projectionOptions,
        CancellationToken token)
    {
        await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);

        // This can only cause problems, so shut this down!
        sessionOptions.AllowAnyTenant = true;

        // Opt into identity mechanics for Event Projections that require that
        if (projectionOptions.EnableDocumentTrackingByIdentity)
        {
            sessionOptions.Tracking = DocumentTracking.IdentityOnly;
        }
        else
        {
            sessionOptions.Tracking = DocumentTracking.None;
        }

        var session = (DocumentSessionBase)OpenSession(sessionOptions);
        var batch = new ProjectionUpdateBatch(Options.Projections, session, ShardExecutionMode.Rebuild, token)
        {
            ShouldApplyListeners = mode == ShardExecutionMode.Continuous && range.Events.Any()
        };

        if (mode == ShardExecutionMode.Continuous)
        {
            await batch.SpinUpMessageBatchAsync(session).ConfigureAwait(false);
        }

        var projectionBatch = new ProjectionBatch(session, batch, mode);

        await projectionBatch.RecordProgress(range).ConfigureAwait(false);

        // #4788: when rebuilding a snapshot whose aggregate has a [NaturalKey], re-emit the
        // natural-key upserts for this page's events. ApplyAsync on the inline path drives off
        // newly-appended StreamActions and never fires during rebuild — without this hook the
        // mt_natural_key_X table stays empty after teardown. Routing the upsert SQL through
        // ProjectionBatch.SessionForTenant returns a ProjectionDocumentSession whose work-tracker
        // IS the ProjectionUpdateBatch, so the operations flush alongside the rebuilt snapshots
        // inside the same batch transaction.
        if (mode == ShardExecutionMode.Rebuild && range.Events.Any())
        {
            var naturalKeySource = Options.Projections.All
                .FirstOrDefault(s => s.Name.EqualsIgnoreCase(range.ShardName.Name))
                as IAggregateProjection;
            if (naturalKeySource?.NaturalKeyDefinition == null)
            {
                naturalKeySource = null;
            }

            if (naturalKeySource != null)
            {
                var naturalKeyProjection = new NaturalKeyProjection(Options.EventGraph, naturalKeySource.NaturalKeyDefinition!);
                foreach (var byTenant in range.Events.GroupBy(e => e.TenantId ?? StorageConstants.DefaultTenantId))
                {
                    var ops = projectionBatch.SessionForTenant(byTenant.Key);
                    naturalKeyProjection.QueueUpsertsForEvents(ops, byTenant);
                }
            }
        }

        return projectionBatch;
    }

    IEventLoader IEventStore<IDocumentOperations, IQuerySession>.BuildEventLoader(IEventDatabase database,
        ILogger loggerFactory, EventFilterable filtering, AsyncOptions shardOptions)
    {
        var filters = buildEventLoaderFilters(filtering).ToArray();
        var inner = new EventLoader(this, (MartenDatabase)database, shardOptions, filters);
        return new ResilientEventLoader(Options.ResiliencePipeline, inner, database);
    }

    /// <summary>
    /// #4596 Phase 2c — consume jasperfx#407 Phase 2c's 5-arg BuildEventLoader
    /// overload, which threads <see cref="ShardName"/> (and therefore the
    /// shard's tenant slot) into the loader. Per-tenant rebuilds rebind the
    /// shard via <c>ShardName.ForTenant(tenantId)</c> before reaching here, so
    /// the loader can add a literal <c>d.tenant_id = '{tenantId}'</c> predicate
    /// to its SQL — partition-pruning <c>mt_events</c> AND ensuring the
    /// per-tenant execution never sees another tenant's events (which would
    /// route doc writes via <see cref="IEvent.TenantId"/> to the wrong tenant
    /// on a rebuild for someone else). The 4-arg explicit-interface override
    /// above stays compiled in case any caller (or out-of-tree composite stage)
    /// still routes through it.
    /// </summary>
    IEventLoader IEventStore<IDocumentOperations, IQuerySession>.BuildEventLoader(IEventDatabase database,
        ILogger loggerFactory, EventFilterable filtering, AsyncOptions shardOptions, ShardName shardName)
    {
        var filters = buildEventLoaderFilters(filtering).ToArray();
        var inner = new EventLoader(this, (MartenDatabase)database, shardOptions, filters, shardName);
        return new ResilientEventLoader(Options.ResiliencePipeline, inner, database);
    }

    private IEnumerable<ISqlFragment> buildEventLoaderFilters(EventFilterable filterable)
    {
        if (filterable.IncludedEventTypes.Any() && !filterable.IncludedEventTypes.Any(x => x.IsAbstract || x.IsInterface))
        {
            // We want to explicitly add in the archived event
            var allTypes = filterable.IncludedEventTypes.Concat([typeof(Archived)]).ToArray();
            if (filterable is IAggregateProjection aggregateProjection)
            {
                var compactedType = typeof(Compacted<>).MakeGenericType(aggregateProjection.AggregateType);
                allTypes = allTypes.Concat([compactedType])
                    .ToArray();
            }

            yield return new EventTypeFilter(Options.EventGraph, allTypes);


        }

        if (filterable.StreamType != null)
        {
            yield return new AggregateTypeFilter(filterable.StreamType, Options.EventGraph);
        }

        if (!filterable.IncludeArchivedEvents)
        {
            yield return IsNotArchivedFilter.Instance;
        }

        if (Options.EventGraph.EnableEventSkippingInProjectionsOrSubscriptions)
        {
            yield return IsNotSkippedFilter.Instance;
        }
    }

    IDocumentOperations IEventStore<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database)
    {
        return LightweightSession(SessionOptions.ForDatabase((IMartenDatabase)database));
    }

    IDocumentOperations IEventStore<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database,
        string tenantId)
    {
        return LightweightSession(SessionOptions.ForDatabase(tenantId, (IMartenDatabase)database));
    }

    ErrorHandlingOptions IEventStore<IDocumentOperations, IQuerySession>.ErrorHandlingOptions(ShardExecutionMode mode)
    {
        return mode == ShardExecutionMode.Rebuild ? Options.Projections.RebuildErrors : Options.Projections.Errors;
    }

    async Task ISubscriptionRunner<ISubscription>.ExecuteAsync(ISubscription subscription, IEventDatabase database, EventRange range, ShardExecutionMode mode,
        CancellationToken token)
    {
        var db = (IMartenDatabase)database;
        await using var parent = (DocumentSessionBase)OpenSession(SessionOptions.ForDatabase(db));

        var batch = new ProjectionUpdateBatch(Options.Projections, parent, mode, token)            {
            ShouldApplyListeners = mode == ShardExecutionMode.Continuous && range.Events.Any()
        };;

        // Mark the progression
        await batch.Queue.PostAsync(range.BuildProgressionOperation(Events)).ConfigureAwait(false);

        await using var session = new ProjectionDocumentSession(this, batch,
            new SessionOptions
            {
                Tracking = DocumentTracking.IdentityOnly,
                Tenant = new Tenant(StorageConstants.DefaultTenantId, db)
            }, mode);


        var listener = await subscription.ProcessEventsAsync(range, range.Agent, session, token)
            .ConfigureAwait(false);

        batch.Listeners.Add(listener);
        await batch.WaitForCompletion().ConfigureAwait(false);

        // Polly is already around the basic retry here, so anything that gets past this
        // probably deserves a full circuit break
        await session.ExecuteBatchAsync(batch, token).ConfigureAwait(false);
    }

    async Task<EventStoreUsage?> IEventStore.TryCreateUsage(CancellationToken token)
    {
        var usage = new EventStoreUsage(Subject, this)
        {
            Database = await Options.Tenancy.DescribeDatabasesAsync(token).ConfigureAwait(false)
        };

        // MaxEventSequence is a single-valued descriptor; in multi-database setups
        // the per-database max isn't representable here, so leave it null.
        if (Options.Tenancy.Cardinality == DatabaseCardinality.Single)
        {
            try
            {
                var defaultDb = (MartenDatabase)Options.Tenancy.Default.Database;
                usage.MaxEventSequence = await defaultDb.FetchMaxEventSequenceAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Storage may not exist yet (no events table) — leave null.
            }
        }

        Options.Projections.Describe(usage, this);

        // JasperFx/ProductSupport#3 — surface the async-daemon error-handling
        // policy on the wire so monitoring tools (CritterWatch) can render the
        // right "shard halts on error" vs "view related dead letters" affordance.
        // Normal-run + rebuild-mode are mirrored as separate descriptors because
        // they carry independent ErrorHandlingOptions on the store.
        usage.ProjectionErrors = new ProjectionErrorHandlingDescriptor
        {
            SkipApplyErrors = Options.Projections.Errors.SkipApplyErrors,
            SkipUnknownEvents = Options.Projections.Errors.SkipUnknownEvents,
            SkipSerializationErrors = Options.Projections.Errors.SkipSerializationErrors
        };
        usage.ProjectionRebuildErrors = new ProjectionErrorHandlingDescriptor
        {
            SkipApplyErrors = Options.Projections.RebuildErrors.SkipApplyErrors,
            SkipUnknownEvents = Options.Projections.RebuildErrors.SkipUnknownEvents,
            SkipSerializationErrors = Options.Projections.RebuildErrors.SkipSerializationErrors
        };

        foreach (var eventMapping in Options.EventGraph.AllEvents())
        {
            var descriptor =
                new EventDescriptor(eventMapping.EventTypeName, TypeDescriptor.For(eventMapping.DocumentType));

            usage.Events.Add(descriptor);
        }

        // DCB tag-type registrations — flatten ITagTypeRegistration onto the
        // wire-friendly TagTypeDescriptor (4 strings; configuration shape only,
        // no row counts).
        foreach (var registration in Options.EventGraph.TagTypes)
        {
            usage.TagTypes.Add(new TagTypeDescriptor
            {
                TagType = registration.TagType.FullName ?? registration.TagType.Name,
                SimpleType = registration.SimpleType.FullName ?? registration.SimpleType.Name,
                TableSuffix = registration.TableSuffix,
                AggregateType = registration.AggregateType?.FullName,
            });

            // Richer descriptor used by the event store explorer's tag-list view —
            // carries the strong TypeDescriptor and an operator-facing description
            // (currently null until Marten exposes a description on the registration).
            usage.DcbTagTypes.Add(new DcbTagDescriptor(
                registration.TagType.Name,
                registration.SimpleType.FullName ?? registration.SimpleType.Name,
                TypeDescriptor.For(registration.TagType),
                Description: null));
        }

        // Configured event-type registrations — surface every event alias the store
        // knows about so the explorer can render the registered event surface.
        foreach (var eventMapping in Options.EventGraph.AllEvents())
        {
            usage.RegisteredEventTypes.Add(new EventTypeDescriptor(
                TypeDescriptor.For(eventMapping.DocumentType),
                eventMapping.EventTypeName,
                Description: null));
        }

        // Aggregates that live outside the multi-tenant boundary in tenanted
        // setups — flat list of CLR type identities.
        foreach (var aggregateType in Options.EventGraph.GlobalAggregates)
        {
            usage.GlobalAggregates.Add(TypeDescriptor.For(aggregateType));
        }

        // jasperfx#475 — advertise WHICH event/stream metadata this store captures
        // so store-aware consumers (CritterWatch) can gate query facets by what is
        // actually persisted, rather than sniffing Marten's MetadataConfig directly.
        // The event flags are opt-in columns and map straight off MetadataConfig.
        // The stream flags are universal in Marten (mt_streams always exposes the
        // aggregate-type / version / created+updated / tenant / archived columns),
        // so they keep the EventMetadataCapabilities defaults of true.
        var metadata = Options.EventGraph.MetadataConfig;
        usage.EventMetadata = new EventMetadataCapabilities
        {
            StoreType = "Marten",
            CorrelationId = metadata.CorrelationIdEnabled,
            CausationId = metadata.CausationIdEnabled,
            Headers = metadata.HeadersEnabled,
            UserName = metadata.UserNameEnabled
        };

        return usage;
    }

    public Uri Subject { get; internal set; } = new Uri("marten://main");

    IReadOnlyEventStore IEventStore.OpenReadOnlyEventStore()
    {
        var session = QuerySession();
        return (IReadOnlyEventStore)session.Events;
    }

    async Task IEventStore.CompactStreamAsync(Guid streamId, CancellationToken token)
    {
        await using var session = LightweightSession();
        var state = await session.Events.FetchStreamStateAsync(streamId, token).ConfigureAwait(false);
        if (state?.AggregateType == null)
        {
            throw new InvalidOperationException(
                $"Cannot compact stream {streamId}: stream not found or no aggregate type associated.");
        }

        var method = typeof(Marten.Events.IEventStoreOperations).GetMethod(nameof(Marten.Events.IEventStoreOperations.CompactStreamAsync),
            [typeof(Guid), typeof(Action<>).MakeGenericType(typeof(StreamCompactingRequest<>).MakeGenericType(state.AggregateType))]);
        var genericMethod = method!.MakeGenericMethod(state.AggregateType);
        await ((Task)genericMethod.Invoke(session.Events, [streamId, null])!).ConfigureAwait(false);
    }

    async Task IEventStore.CompactStreamAsync(string streamKey, CancellationToken token)
    {
        await using var session = LightweightSession();
        var state = await session.Events.FetchStreamStateAsync(streamKey, token).ConfigureAwait(false);
        if (state?.AggregateType == null)
        {
            throw new InvalidOperationException(
                $"Cannot compact stream '{streamKey}': stream not found or no aggregate type associated.");
        }

        var method = typeof(Marten.Events.IEventStoreOperations).GetMethod(nameof(Marten.Events.IEventStoreOperations.CompactStreamAsync),
            [typeof(string), typeof(Action<>).MakeGenericType(typeof(StreamCompactingRequest<>).MakeGenericType(state.AggregateType))]);
        var genericMethod = method!.MakeGenericMethod(state.AggregateType);
        await ((Task)genericMethod.Invoke(session.Events, [streamKey, null])!).ConfigureAwait(false);
    }
}
