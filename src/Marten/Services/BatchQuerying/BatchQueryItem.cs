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
    
    [Obsolete(QuerySession.SynchronousRemoval)]
    public void Read(DbDataReader reader, IMartenSession session)
    {
        var result = _handler.Handle(reader, session);
        Completion.SetResult(result);
    }
}
