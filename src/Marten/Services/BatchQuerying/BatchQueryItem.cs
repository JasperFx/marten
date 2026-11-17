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
        var result = await _handler.HandleAsync(reader, session, token).ConfigureAwait(false);
        Completion.SetResult(result);
    }

    public void Fail(Exception exception, CancellationToken token)
    {
        var failed = exception is OperationCanceledException && token.IsCancellationRequested
            ? Completion.TrySetCanceled(token)
            : Completion.TrySetException(exception);

        if (failed)
        {
            // The caller already has this exception from Execute() and may never await the item, so mark it
            // observed rather than let an unawaited item raise TaskScheduler.UnobservedTaskException later.
            _ = Completion.Task.Exception;
        }
    }

}
