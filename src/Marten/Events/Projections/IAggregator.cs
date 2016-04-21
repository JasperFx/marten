using System;

namespace Marten.Events.Projections
{
    public interface IAggregator 
    {
        Type AggregateType { get; }
        string Alias { get; }
    }
}