using System.Collections.Generic;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    public interface IViewProjectionEventSlicer<TDoc, TId>: IEventSlicer<TDoc, TId>
    {
        List<IGrouper<TId>> Groupers { get; }
        List<IFanOutRule> Fanouts { get; }
        List<IGrouperFactory<TId>> GrouperFactories { get; }
    }
}
