using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{

    public enum StreamIdentity
    {
        AsGuid,
        AsString
    }

    public class EventGraph : IFeatureSchema
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
                Options.Storage.AddMapping(mapping);

                return mapping;
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };

            InlineProjections = new ProjectionCollection(options);
            AsyncProjections = new ProjectionCollection(options);            
        }

        public StreamIdentity StreamIdentity { get; set; } = StreamIdentity.AsGuid;

        internal StoreOptions Options { get; }

        internal DbObjectName Table => new DbObjectName(DatabaseSchemaName, "mt_events");


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


        public bool IsActive(StoreOptions options) => _events.Any() || _aggregates.Any();

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }


        public void AddAggregator<T>(IAggregator<T> aggregator) where T : class, new()
        {
            Options.Storage.MappingFor(typeof(T));
            _aggregates.AddOrUpdate(typeof(T), aggregator, (type, previous) => aggregator);
        }

        public IAggregator<T> AggregateFor<T>() where T : class, new()
        {
            return _aggregates
                .GetOrAdd(typeof(T), type =>
                {
                    Options.Storage.MappingFor(typeof(T));
                    return _aggregatorLookup.Lookup<T>();
                })
                .As<IAggregator<T>>();
        }


        public Type AggregateTypeFor(string aggregateTypeName)
        {
            if (_aggregateByName.ContainsKey(aggregateTypeName))
            {
                return _aggregateByName[aggregateTypeName].AggregateType;
            }

            var aggregate = AllAggregates().FirstOrDefault(x => x.Alias == aggregateTypeName);
            if (aggregate == null)
            {
                return null;
            }

            return
                _aggregateByName.GetOrAdd(aggregateTypeName,
                    name => { return AllAggregates().FirstOrDefault(x => x.Alias == name); }).AggregateType;
        }

        public ProjectionCollection InlineProjections { get; }
        public ProjectionCollection AsyncProjections { get; }
        internal DbObjectName ProgressionTable => new DbObjectName(DatabaseSchemaName, "mt_event_progression");

        public string AggregateAliasFor(Type aggregateType)
        {
            return _aggregates
                .GetOrAdd(aggregateType, type => _aggregatorLookup.Lookup(type)).Alias;
        }

        public IProjection ProjectionFor(Type viewType)
        {
            return AsyncProjections.ForView(viewType) ?? InlineProjections.ForView(viewType);
        }

        public ViewProjection<TView, TId> ProjectView<TView, TId>() where TView : class, new()
        {
            var projection = new ViewProjection<TView, TId>();
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

        IEnumerable<Type> IFeatureSchema.DependentTypes()
        {
            yield break;
        }

        ISchemaObject[] IFeatureSchema.Objects
        {
            get
            {
                var eventsTable = new EventsTable(this);
                
                // SAMPLE: using-sequence
                var sequence = new Sequence(new DbObjectName(DatabaseSchemaName, "mt_events_sequence"))
                {
                    Owner = eventsTable.Identifier,
                    OwnerColumn = "seq_id"
                };
                // ENDSAMPLE

                return new ISchemaObject[]
                {
                    new StreamsTable(this),
                    eventsTable,
                    new EventProgressionTable(DatabaseSchemaName), 
                    sequence,  
                    
                    new AppendEventFunction(this), 
                    new SystemFunction(DatabaseSchemaName, "mt_mark_event_progression", "varchar, bigint"), 
                };
            }
        }

        Type IFeatureSchema.StorageType => typeof(EventGraph);
        public string Identifier { get; } = "eventstore";
        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            // Nothing
        }

        internal string GetStreamIdType()
        {
            return StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
        }

        private readonly ConcurrentDictionary<Type, string> _dotnetTypeNames = new ConcurrentDictionary<Type, string>();

        internal string DotnetTypeNameFor(Type type)
        {
            if (!_dotnetTypeNames.ContainsKey(type))
            {
                _dotnetTypeNames[type] = $"{type.FullName}, {type.GetTypeInfo().Assembly.GetName().Name}";
            }

            return _dotnetTypeNames[type];
        }

        private readonly ConcurrentDictionary<string, Type> _nameToType = new ConcurrentDictionary<string, Type>();

        internal Type TypeForDotNetName(string assemblyQualifiedName)
        {
            if (!_nameToType.ContainsKey(assemblyQualifiedName))
            {
                _nameToType[assemblyQualifiedName] = Type.GetType(assemblyQualifiedName);
            }

            return _nameToType[assemblyQualifiedName];
        }
    }
}