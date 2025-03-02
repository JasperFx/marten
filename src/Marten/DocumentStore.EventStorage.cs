using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Progress;
using Marten.Internal.OpenTelemetry;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten;

public partial class DocumentStore: IEventStorage<IDocumentOperations, IQuerySession>
{
    IEventRegistry IEventStorage<IDocumentOperations, IQuerySession>.Registry => Options.EventGraph;

    public Type IdentityTypeForProjectedType(Type aggregateType)
    {
        return new DocumentMapping(aggregateType, Options).DocumentType;
    }

    string IEventStorage<IDocumentOperations, IQuerySession>.DefaultDatabaseName =>
        Options.Tenancy.Default.Database.Identifier;

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.ContinuousErrors =>
        Options.Projections.Errors;

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.RebuildErrors =>
        Options.Projections.RebuildErrors;

    IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> IEventStorage<IDocumentOperations, IQuerySession>.
        AllShards()
    {
        return Options.Projections.AllShards();
    }

    Meter IEventStorage<IDocumentOperations, IQuerySession>.Meter => throw new NotImplementedException();

    ActivitySource IEventStorage<IDocumentOperations, IQuerySession>.ActivitySource => MartenTracing.ActivitySource;

    TimeProvider IEventStorage<IDocumentOperations, IQuerySession>.TimeProvider => Options.Events.TimeProvider;

    string IEventStorage<IDocumentOperations, IQuerySession>.MetricsPrefix => "marten";

    AutoCreate IEventStorage<IDocumentOperations, IQuerySession>.AutoCreateSchemaObjects =>
        Options.AutoCreateSchemaObjects;

    async Task IEventStorage<IDocumentOperations, IQuerySession>.RewindSubscriptionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token,
        long? sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var names = Options
            .Projections
            .AllShards()
            .Where(x => x.Name.ProjectionName.EqualsIgnoreCase(subscriptionName))
            .Select(x => x.Name)
            .ToArray();

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
                    $"update {Options.EventGraph.ProgressionTable} set last_seq_id = ? where name = ?", sequenceFloor,
                    name.Identity);
            }
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x =>
            x.ProjectionName == subscriptionName && x.EventSequence >= sequenceFloor);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    async Task IEventStorage<IDocumentOperations, IQuerySession>.RewindAgentProgressAsync(IEventDatabase database,
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

    async Task IEventStorage<IDocumentOperations, IQuerySession>.TeardownExistingProjectionProgressAsync(
        IEventDatabase database, string subscriptionName, CancellationToken token)
    {
        var sessionOptions = SessionOptions.ForDatabase((IMartenDatabase)database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = LightweightSession(sessionOptions);

        var source = Options.Projections.All.FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(subscriptionName));

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
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.ProjectionName);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    ValueTask<IProjectionBatch<IDocumentOperations, IQuerySession>> IEventStorage<IDocumentOperations, IQuerySession>.
        StartProjectionBatchAsync(EventRange range, IEventDatabase database, ShardExecutionMode mode,
            CancellationToken token)
    {
        throw new NotImplementedException();
    }

    IEventLoader IEventStorage<IDocumentOperations, IQuerySession>.BuildEventLoader(IEventDatabase database,
        ILogger loggerFactory, EventFilterable filtering)
    {
        throw new NotImplementedException();
    }

    IDocumentOperations IEventStorage<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database)
    {
        return LightweightSession(SessionOptions.ForDatabase((IMartenDatabase)database));
    }

    IDocumentOperations IEventStorage<IDocumentOperations, IQuerySession>.OpenSession(IEventDatabase database,
        string tenantId)
    {
        return LightweightSession(SessionOptions.ForDatabase(tenantId, (IMartenDatabase)database));
    }

    ErrorHandlingOptions IEventStorage<IDocumentOperations, IQuerySession>.ErrorHandlingOptions(ShardExecutionMode mode)
    {
        return mode == ShardExecutionMode.Rebuild ? Options.Projections.RebuildErrors : Options.Projections.Errors;
    }
}
