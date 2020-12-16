using System;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4")]
    public interface IHasCustomEventProgressionName
    {
        string Name { get; }
    }
}
