using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    public interface IFanOutRule
    {
        void Apply(List<IEvent> events);
        Type OriginatingType { get; }
    }
}
