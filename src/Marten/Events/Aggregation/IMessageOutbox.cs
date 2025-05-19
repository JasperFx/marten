using System.Threading.Tasks;
using Marten.Internal.Sessions;

namespace Marten.Events.Aggregation;

public interface IMessageOutbox
{
    ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session);
}