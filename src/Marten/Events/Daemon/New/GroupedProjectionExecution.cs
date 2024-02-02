using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Daemon.New;

public class GroupedProjectionExecution: ISubscriptionExecution
{
    private readonly IProjectionSource _source;
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly CancellationTokenSource _cancellation = new();
    private TransformBlock<EventRange, EventRangeGroup>? _grouping;
    private ActionBlock<EventRangeGroup>? _building;
    private SessionOptions _sessionOptions;

    // TODO -- use combined cancellation?
    public GroupedProjectionExecution(IProjectionSource source, DocumentStore store, IMartenDatabase database)
    {
        _source = source;
        _store = store;
        _database = database;
        _sessionOptions = SessionOptions.ForDatabase(_database);

        var singleFileOptions = _cancellation.Token.SequentialOptions();
        _grouping = new TransformBlock<EventRange, EventRangeGroup>(groupEventRange, singleFileOptions);
        _building = new ActionBlock<EventRangeGroup>(processRange, singleFileOptions);
        _grouping.LinkTo(_building);
    }

    private async Task<EventRangeGroup> groupEventRange(EventRange range)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return null;
        }

        return await _source.GroupEvents(_store, _database, range, _cancellation.Token).ConfigureAwait(false);
    }

    private async Task processRange(EventRangeGroup group)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        // This should be done *once* before proceeding
        // And this cannot be put inside of ConfigureUpdateBatch
        // Low chance of errors
        group.Reset();

        await using var session = (DocumentSessionBase)_store.IdentitySession(_sessionOptions!);
        await using var batch = new ProjectionUpdateBatch(_store.Events, _store.Options.Projections, session,
            group.Range, group.Cancellation, Mode);

        await group.ConfigureUpdateBatch(batch).ConfigureAwait(false);

        // TODO -- watch this carefully!!!! This will be errors from trying to apply events
        // you might get transient errors even after the retries
        // More likely, this might be a collection of ApplyEventException, and thus, retry the batch w/ skipped
        // sequences

        await batch.WaitForCompletion().ConfigureAwait(false);

        group.Dispose();

        // Executing the SQL commands for the ProjectionUpdateBatch
        try
        {
            // Polly is already around the basic retry here, so anything that gets past this
            // probably deserves a full circuit break
            await session.ExecuteBatchAsync(batch, _cancellation.Token).ConfigureAwait(false);

            // TODO -- log here obviously
            // Logger.LogInformation("Shard '{ProjectionShardIdentity}': Executed updates for {Range}",
            //     ProjectionShardIdentity, batch.Range);
        }
        catch (Exception e)
        {
            if (!_cancellation.IsCancellationRequested)
            {
                // TODO -- log here obviously
                // Logger.LogError(e,
                //     "Failure in shard '{ProjectionShardIdentity}' trying to execute an update batch for {Range}",
                //     ProjectionShardIdentity,
                //     batch.Range);
                throw;
            }
        }
    }

    public ShardExecutionMode Mode { get; set; } = ShardExecutionMode.Continuous;


    public async ValueTask DisposeAsync()
    {
        throw new System.NotImplementedException();
    }

    public async ValueTask StopAsync()
    {
        throw new System.NotImplementedException();
    }

    public void Enqueue(EventPage range, SubscriptionAgent subscriptionAgent)
    {
        throw new System.NotImplementedException();
    }
}
