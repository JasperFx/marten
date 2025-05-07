using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
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

namespace Marten;

public partial class DocumentStore: IEventStore<IDocumentOperations, IQuerySession>, ISubscriptionRunner<ISubscription>
{
    static DocumentStore()
    {
        ProjectionExceptions.RegisterTransientExceptionType<NpgsqlException>();
        ProjectionExceptions.RegisterTransientExceptionType<MartenCommandException>();
    }

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

    Meter IEventStore<IDocumentOperations, IQuerySession>.Meter => Options.OpenTelemetry.Meter;

    ActivitySource IEventStore<IDocumentOperations, IQuerySession>.ActivitySource => MartenTracing.ActivitySource;

    TimeProvider IEventStore<IDocumentOperations, IQuerySession>.TimeProvider => Options.Events.TimeProvider;

    string IEventStore<IDocumentOperations, IQuerySession>.MetricsPrefix => "marten";

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

        if (!names.Any())
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

    async Task IEventStore<IDocumentOperations, IQuerySession>.TeardownExistingProjectionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(subscriptionName));

        if (source == null)
        {
            throw new ArgumentOutOfRangeException(nameof(subscriptionName));
        }

        if (source.Options.TeardownDataOnRebuild)
        {
            source.Options.Teardown(session);
        }

        foreach (var agent in source.Shards())
            session.QueueOperation(new DeleteProjectionProgress(Events, agent.Name.Identity));

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.Name);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async ValueTask<IProjectionBatch<IDocumentOperations, IQuerySession>> StartProjectionBatchAsync(
        EventRange range, IEventDatabase database, ShardExecutionMode mode,
        AsyncOptions projectionOptions,
        CancellationToken token)
    {
        await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);

        // Opt into identity mechanics for Event Projections that require that
        if (projectionOptions.EnableDocumentTrackingByIdentity)
        {
            sessionOptions.Tracking = DocumentTracking.IdentityOnly;
        }

        var session = (DocumentSessionBase)IdentitySession(sessionOptions);
        var batch = new ProjectionUpdateBatch(Options.Projections, session, ShardExecutionMode.Rebuild, token)
        {
            ShouldApplyListeners = mode == ShardExecutionMode.Continuous && range.Events.Any()
        };

        var projectionBatch = new ProjectionBatch(session, batch, mode);
        if (range.SequenceFloor == 0)
        {
            batch.Queue.Post(new InsertProjectionProgress(session.Options.EventGraph, range));
        }
        else
        {
            batch.Queue.Post(new UpdateProjectionProgress(session.Options.EventGraph, range));
        }

        return projectionBatch;
    }

    IEventLoader IEventStore<IDocumentOperations, IQuerySession>.BuildEventLoader(IEventDatabase database,
        ILogger loggerFactory, EventFilterable filtering, AsyncOptions shardOptions)
    {
        var filters = buildEventLoaderFilters(filtering).ToArray();
        var inner = new EventLoader(this, (MartenDatabase)database, shardOptions, filters);
        return new ResilientEventLoader(Options.ResiliencePipeline, inner);
    }

    private IEnumerable<ISqlFragment> buildEventLoaderFilters(EventFilterable filterable)
    {
        if (filterable.IncludedEventTypes.Any() && !filterable.IncludedEventTypes.Any(x => x.IsAbstract || x.IsInterface))
        {
            // We want to explicitly add in the archived event
            var allTypes = filterable.IncludedEventTypes.Concat([typeof(Archived)]).ToArray();
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
        batch.Queue.Post(range.BuildProgressionOperation(Events));

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

        Options.Projections.Describe(usage);

        foreach (var eventMapping in Options.EventGraph.AllEvents())
        {
            var descriptor =
                new EventDescriptor(eventMapping.EventTypeName, TypeDescriptor.For(eventMapping.DocumentType));

            usage.Events.Add(descriptor);
        }

        return usage;
    }

    public Uri Subject { get; internal set; } = new Uri("marten://main");
}
