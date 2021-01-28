using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    internal interface IFanOutRule
    {
        void Apply(List<IEvent> events);
        Type OriginatingType { get; }
    }
}
