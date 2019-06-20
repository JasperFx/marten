using System;
using Baseline;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Default IAggregator lookup strategy. Defaults to <see cref="Aggregator{T}"/>
    /// </summary>
    public sealed class AggregatorLookup: IAggregatorLookup
    {
        private readonly Func<Type, IAggregator> factory;

        /// <param name="factory">Factory for resolving IAggregator for the supplied type</param>
        public AggregatorLookup(Func<Type, IAggregator> factory = null)
        {
            this.factory = factory;
        }

        public IAggregator<T> Lookup<T>() where T : class, new()
        {
            // trade null check for the cost of using default factory with Activator.CreateInstance
            return (IAggregator<T>)factory?.Invoke(typeof(T)) ?? new Aggregator<T>();
        }

        public IAggregator Lookup(Type aggregateType)
        {
            return factory?.Invoke(aggregateType) ?? typeof(Aggregator<>).CloseAndBuildAs<IAggregator>(aggregateType);
        }
    }
}
