using JasperFx.Events;

namespace Marten.Events.Aggregation;

public interface IMessageBatch: IMessageSink, IChangeListener
{

}
