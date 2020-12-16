using System;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Used by <see cref="EventGraph"/> to resolve IAggregator when no explicit IAggregator registration exists
    /// </summary>
    [Obsolete("This will be eliminated in V4")]
    public interface IAggregatorLookup
    {
        /// <summary>
        /// Resolve aggregator for T
        /// </summary>
        IAggregator<T> Lookup<T>() where T : class;

        /// <summary>
        /// Resolve aggregator for aggregateType
        /// </summary>
        IAggregator Lookup(Type aggregateType);
    }
}
