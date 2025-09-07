using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Marten.Events.Aggregation;

public interface IMessageBatch: IMessageSink, IChangeListener
{

}
