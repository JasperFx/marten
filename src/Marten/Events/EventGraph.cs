using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Events
{
    public class EventGraph
    {
        private readonly ConcurrentDictionary<string, IAggregator> _aggregateByName =
            new ConcurrentDictionary<string, IAggregator>();

        private readonly ConcurrentDictionary<Type, IAggregator> _aggregates =
            new ConcurrentDictionary<Type, IAggregator>();

        private readonly ConcurrentCache<string, EventMapping> _byEventName = new ConcurrentCache<string, EventMapping>();
        private readonly ConcurrentCache<Type, EventMapping> _events = new ConcurrentCache<Type, EventMapping>();

        private IAggregatorLookup _aggregatorLookup;
        private string _databaseSchemaName;

        public EventGraph(StoreOptions options)
        {
            Options = options;
            _aggregatorLookup = new AggregatorLookup();
            _events.OnMissing = eventType =>
            {
                var mapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
                Options.AddMapping(mapping);

                return mapping;
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };

            SchemaObjects = new EventStoreDatabaseObjects(this);

            InlineProjections = new ProjectionCollection(options);
            AsyncProjections = new ProjectionCollection(options);            
        }

        internal StoreOptions Options { get; }

        internal TableName Table => new TableName(DatabaseSchemaName, "mt_events");

        internal IDocumentSchemaObjects SchemaObjects { get; }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : class, new()
        {
            return EventMappingFor(typeof(T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }

        public IEnumerable<IAggregator> AllAggregates()
        {
            return _aggregates.Values;
        }

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }

        public void AddEventType(Type eventType)
        {
            _events.FillDefault(eventType);
        }

        public void AddEventTypes(IEnumerable<Type> types)
        {
            types.Each(AddEventType);
        }


        public bool IsActive => _events.Any() || _aggregates.Any();

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }


        public void AddAggregator<T>(IAggregator<T> aggregator) where T : class, new()
        {
            Options.MappingFor(typeof(T));
            _aggregates.AddOrUpdate(typeof(T), aggregator, (type, previous) => aggregator);
        }

        public IAggregator<T> AggregateFor<T>() where T : class, new()
        {
            return _aggregates
                .GetOrAdd(typeof(T), type =>
                {
                    Options.MappingFor(typeof(T));
                    return _aggregatorLookup.Lookup<T>();
                })
                .As<IAggregator<T>>();
        }


        public Type AggregateTypeFor(string aggregateTypeName)
        {
            return
                _aggregateByName.GetOrAdd(aggregateTypeName,
                    name => { return AllAggregates().FirstOrDefault(x => x.Alias == name); }).AggregateType;
        }

        public ProjectionCollection InlineProjections { get; }
        public ProjectionCollection AsyncProjections { get; }
        internal TableName ProgressionTable => new TableName(DatabaseSchemaName, "mt_event_progression");

        public string AggregateAliasFor(Type aggregateType)
        {
            return _aggregates
                .GetOrAdd(aggregateType, type => _aggregatorLookup.Lookup(type)).Alias;
        }

        public IProjection ProjectionFor(Type viewType)
        {
            return AsyncProjections.ForView(viewType) ?? InlineProjections.ForView(viewType);
        }

        public ViewProjection<TView> ProjectView<TView>() where TView : class, new()
        {
            var projection = new ViewProjection<TView>();
            InlineProjections.Add(projection);
            return projection;
        }

        /// <summary>
        /// Set default strategy to lookup IAggregator when no explicit IAggregator registration exists. 
        /// </summary>
        /// <remarks>Unless called, <see cref="AggregatorLookup"/> is used</remarks>
        public void UseAggregatorLookup(IAggregatorLookup aggregatorLookup)
        {
            _aggregatorLookup = aggregatorLookup;
        }
    }
}