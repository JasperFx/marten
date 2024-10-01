using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Weasel.Core;

namespace Marten.Events.Daemon.Internals;

public class GroupedProjectionExecution: ISubscriptionExecution
{
    private readonly IProjectionSource _source;
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TransformBlock<EventRange, EventRangeGroup> _grouping;
    private readonly ActionBlock<EventRangeGroup> _building;
    private readonly SessionOptions _sessionOptions;

    public GroupedProjectionExecution(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database,
        ILogger logger)
    {
        _source = shard.Source;
        _store = store;
        _database = database;
        _logger = logger;
        _sessionOptions = SessionOptions.ForDatabase(_database);

        var singleFileOptions = _cancellation.Token.SequentialOptions();
        _grouping = new TransformBlock<EventRange, EventRangeGroup>(groupEventRange, singleFileOptions);
        _building = new ActionBlock<EventRangeGroup>(processRange, singleFileOptions);
        _grouping.LinkTo(_building, x => x != null);

        ProjectionShardIdentity = shard.Name.Identity;
        if (database.Identifier != "Marten")
        {
            ProjectionShardIdentity += $"@{database.Identifier}";
        }
    }

    public ShardExecutionMode Mode { get; set; }
    public bool TryBuildReplayExecutor(out IReplayExecutor executor)
    {
        if (_store.Events.UseOptimizedProjectionRebuilds && _source.TryBuildReplayExecutor(_store, _database, out executor)) return true;

        executor = default;
        return false;
    }

    public string ProjectionShardIdentity { get; }

    public string DatabaseName => _database.Identifier;

    public async Task EnsureStorageExists()
    {
        if (_store.Options.AutoCreateSchemaObjects == AutoCreate.None) return;

        foreach (var storageType in _source.Options.StorageTypes)
        {
            await _database.EnsureStorageExistsAsync(storageType, _cancellation.Token).ConfigureAwait(false);
        }

        foreach (var publishedType in _source.PublishedTypes())
        {
            await _database.EnsureStorageExistsAsync(publishedType, _cancellation.Token).ConfigureAwait(false);
        }
    }

    private async Task<EventRangeGroup> groupEventRange(EventRange range)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return null;
        }

        using var activity = range.Agent.Metrics.TrackGrouping(range);

        try
        {
            var group = await _source.GroupEvents(_store, _database, range, _cancellation.Token).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Debug) && Mode == ShardExecutionMode.Continuous)
            {
                _logger.LogDebug(
                    "Subscription {Name} successfully grouped {Number} events with a floor of {Floor} and ceiling of {Ceiling}",
                    ProjectionShardIdentity, range.Events.Count, range.SequenceFloor, range.SequenceCeiling);
            }

            return group;
        }
        catch (Exception e)
        {
            activity?.RecordException(e);
            _logger.LogError(e, "Failure trying to group events for {Name} from {Floor} to {Ceiling}",
                ProjectionShardIdentity, range.SequenceFloor, range.SequenceCeiling);
            await range.Agent.ReportCriticalFailureAsync(e).ConfigureAwait(false);

            return null;
        }
        finally
        {
            activity?.Stop();
        }
    }

    private async Task processRange(EventRangeGroup group)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        using var activity = group.Range.Agent.Metrics.TrackExecution(group.Range);

        try
        {
            await using var session = (DocumentSessionBase)_store.IdentitySession(_sessionOptions!);

            // This should be done *once* before proceeding
            // And this cannot be put inside of ConfigureUpdateBatch
            // Low chance of errors
            group.Reset();

            var options = Mode == ShardExecutionMode.Continuous
                ? _store.Options.Projections.Errors
                : _store.Options.Projections.RebuildErrors;

            var batch = options.SkipApplyErrors
                ? await buildBatchWithSkipping(group, session, _cancellation.Token).ConfigureAwait(false)
                : await buildBatchAsync(group, session).ConfigureAwait(false);

            // Executing the SQL commands for the ProjectionUpdateBatch
            await applyBatchOperationsToDatabaseAsync(group, session, batch).ConfigureAwait(false);

            group.Agent.Metrics.UpdateProcessed(group.Range.Size);
        }
        catch (Exception e)
        {
            activity?.RecordException(e);
            _logger.LogError(e,
                "Error trying to build and apply changes to event subscription {Name} from {Floor} to {Ceiling}",
                ProjectionShardIdentity, group.Range.SequenceFloor, group.Range.SequenceCeiling);
            await group.Agent.ReportCriticalFailureAsync(e).ConfigureAwait(false);
        }
        finally
        {
            activity?.Stop();
        }
    }

    private async Task applyBatchOperationsToDatabaseAsync(EventRangeGroup group, DocumentSessionBase session,
        ProjectionUpdateBatch batch)
    {
        try
        {
            // Polly is already around the basic retry here, so anything that gets past this
            // probably deserves a full circuit break
            await session.ExecuteBatchAsync(batch, _cancellation.Token).ConfigureAwait(false);

            group.Agent.MarkSuccess(group.Range.SequenceCeiling);

            if (Mode == ShardExecutionMode.Continuous)
            {
                _logger.LogInformation("Shard '{ProjectionShardIdentity}': Executed updates for {Range}",
                    ProjectionShardIdentity, group.Range);
            }
        }
        catch (Exception e)
        {
            if (!_cancellation.IsCancellationRequested)
            {
                _logger.LogError(e,
                    "Failure in shard '{ProjectionShardIdentity}' trying to execute an update batch for {Range}",
                    ProjectionShardIdentity,
                    group.Range);
                throw;
            }
        }
        finally
        {
            await batch.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<ProjectionUpdateBatch> buildBatchWithSkipping(EventRangeGroup group, DocumentSessionBase session,
        CancellationToken cancellationToken)
    {
        ProjectionUpdateBatch batch = null;
        while (batch == null && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                batch = await buildBatchAsync(group, session).ConfigureAwait(false);
            }
            catch (ApplyEventException e)
            {
                await group.SkipEventSequence(e.Event.Sequence, _database).ConfigureAwait(false);
                await group.Agent.RecordDeadLetterEventAsync(new DeadLetterEvent(e.Event, group.Range.ShardName, e)).ConfigureAwait(false);
            }
        }

        return batch;
    }

    private async Task<ProjectionUpdateBatch> buildBatchAsync(EventRangeGroup group, DocumentSessionBase session)
    {
        ProjectionUpdateBatch batch = default;
        try
        {
            batch = new ProjectionUpdateBatch(_store.Options.Projections, session, group.Agent.Mode, group.Cancellation)
            {
                ShouldApplyListeners = group.Agent.Mode == ShardExecutionMode.Continuous && group.Range.Events.Any()
            };

            // Mark the progression
            batch.Queue.Post(group.Range.BuildProgressionOperation(_store.Events));

            await group.ConfigureUpdateBatch(batch).ConfigureAwait(false);
            await batch.WaitForCompletion().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // TODO -- watch this carefully!!!! This will be errors from trying to apply events
            // you might get transient errors even after the retries
            // More likely, this might be a collection of ApplyEventException, and thus, retry the batch w/ skipped
            // sequences

            _logger.LogError(e, "Subscription {Name} failed while creating a SQL batch for updates for events from {Floor} to {Ceiling}", ProjectionShardIdentity, group.Range.SequenceFloor, group.Range.SequenceCeiling);

            await batch!.DisposeAsync().ConfigureAwait(false);

            throw;
        }
        finally
        {
            // Clean up the group, release sessions. TODO -- find a way to eliminate this
            group.Dispose();
        }

        return batch;
    }

    public async ValueTask DisposeAsync()
    {
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif

        _grouping.Complete();
        _building.Complete();
    }

    public void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent)
    {
        if (_cancellation.IsCancellationRequested) return;

        var range = new EventRange(subscriptionAgent.Name, page.Floor, page.Ceiling)
        {
            Agent = subscriptionAgent,
            Events = page
        };

        _grouping.Post(range);
    }

    public async Task StopAndDrainAsync(CancellationToken token)
    {
        _grouping.Complete();
        await _grouping.Completion.ConfigureAwait(false);
        _building.Complete();
        await _building.Completion.ConfigureAwait(false);

#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
    }

    public async Task HardStopAsync()
    {
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
        _grouping.Complete();
        _building.Complete();
    }
}
