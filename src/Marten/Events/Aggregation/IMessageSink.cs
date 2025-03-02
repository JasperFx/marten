using System.Threading.Tasks;

namespace Marten.Events.Aggregation;

public interface IMessageSink
{
    ValueTask PublishAsync<T>(T message);
}