using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Exceptions;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon;

internal class RebuildingEventFetcher: EventFetcher
{
    private readonly BatchBlock<DeadLetterEvent> _batching;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ShardAgent _shardAgent;
    private readonly IDocumentStore _store;
    private readonly ActionBlock<DeadLetterEvent[]> _storing;
    private long _serializationErrorCount;

    public RebuildingEventFetcher(IDocumentStore store, ShardAgent shardAgent, IMartenDatabase database,
        ISqlFragment[] filters): base(store, shardAgent, database, filters)
    {
        _store = store;
        _shardAgent = shardAgent;

        _batching = new BatchBlock<DeadLetterEvent>(100);
        _storing = new ActionBlock<DeadLetterEvent[]>(persistLetters,
            new ExecutionDataflowBlockOptions
            {
                CancellationToken = _cancellation.Token, EnsureOrdered = true, MaxDegreeOfParallelism = 1
            });

        _batching.LinkTo(_storing);
    }

    private async Task persistLetters(DeadLetterEvent[] arg)
    {
        _serializationErrorCount += arg.Length;
        try
        {
            await _store.BulkInsertDocumentsAsync(arg, cancellation: _cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (!_cancellation.IsCancellationRequested)
            {
                await Task.Delay(100, _cancellation.Token).ConfigureAwait(false);
                if (!_cancellation.IsCancellationRequested)
                {
                    _storing.Post(arg); // retry
                }
            }
        }
    }

    protected override async Task handleEvent(EventRange range, CancellationToken token, DbDataReader reader)
    {
        try
        {
            await base.handleEvent(range, token, reader).ConfigureAwait(false);
        }
        catch (EventDeserializationFailureException e)
        {
            _shardAgent.Logger.LogError(e, "Error de-serializing event {Sequence}", e.Sequence);
            var deadLetter = e.ToDeadLetterEvent(_shardAgent.Name);
            _batching.Post(deadLetter);
        }
    }

    public async Task<long> Complete()
    {
        _batching.Complete();
        await _batching.Completion.ConfigureAwait(false);
        _storing.Complete();
        await _storing.Completion.ConfigureAwait(false);

        return _serializationErrorCount;
    }
}
