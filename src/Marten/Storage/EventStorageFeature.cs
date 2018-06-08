using System;
using System.Collections.Concurrent;
using Baseline;
using Marten.Events;

namespace Marten.Storage {
    internal class EventStorageFeatures
    {
        private readonly ConcurrentDictionary<Type, EventMapping> _eventMappings = new ConcurrentDictionary<Type, EventMapping>();
        internal EventMapping MappingFor(Type eventType, StoreOptions options)
        {
            return _eventMappings.GetOrAdd(eventType, type => typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(options.Events, eventType));
        }
    }
}