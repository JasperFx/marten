#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying;

internal class BatchQueryItem<T>: IBatchQueryItem
{
    private readonly IQueryHandler<T> _handler;

    public BatchQueryItem(IQueryHandler<T> handler)
    {
        _handler = handler;

        Completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }


    public TaskCompletionSource<T> Completion { get; }
    public Task<T> Result => Completion.Task;

    public IQueryHandler Handler => _handler;

    public async Task ReadAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        if (_handler is IFinishesAfterTheReader<T> finishesLater)
        {
            await finishesLater.ReadAsync(reader, session, token).ConfigureAwait(false);
            return;
        }

        var result = await _handler.HandleAsync(reader, session, token).ConfigureAwait(false);
        Completion.SetResult(result);
    }

    public async Task FinishAsync(IMartenSession session, CancellationToken token)
    {
        if (_handler is IFinishesAfterTheReader<T> finishesLater)
        {
            Completion.SetResult(await finishesLater.FinishAsync(session, token).ConfigureAwait(false));
        }
    }

}
