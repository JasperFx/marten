using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Services;

namespace Marten.Events.Aggregation;

internal class NulloMessageOutbox: IMessageOutbox, IMessageBatch
{
    public ValueTask PublishAsync<T>(T message, string tenantId)
    {
        return new ValueTask();
    }

    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
    {
        return new ValueTask<IMessageBatch>(this);
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        return Task.CompletedTask;
    }
}
