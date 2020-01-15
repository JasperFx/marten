using System;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Used by <see cref="EventGraph"/> to resolve IAggregator when no explicit IAggregator registration exists
    /// </summary>
    public interface IAggregatorLookup
    {
        /// <summary>
        /// Resolve aggregator for T
        /// </summary>
        IAggregator<T> Lookup<T>() where T : class, new();

        /// <summary>
        /// Resolve aggregator for aggregateType
        /// </summary>
        IAggregator Lookup(Type aggregateType);
    }
}
