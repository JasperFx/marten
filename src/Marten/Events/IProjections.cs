using System.Collections.Generic;
using Marten.Events.Projections;

namespace Marten.Events
{
    public interface IProjections
    {
        IList<IProjection> Inlines { get; }
    }
}