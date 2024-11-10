using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Services;
using Weasel.Core.Operations;

namespace Marten.Events.Aggregation;

public interface IMessageSink
{
    ValueTask PublishAsync<T>(T message);
}

public interface IMessageBatch: IMessageSink, IChangeListener
{

}

public interface IMessageOutbox
{
    ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session);
}

internal class NulloMessageOutbox: IMessageOutbox, IMessageBatch
{
    public ValueTask PublishAsync<T>(T message)
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
