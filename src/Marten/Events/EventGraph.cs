using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
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
                throw new ArgumentOutOfRangeException(nameof(aggregateTypeName), $"Unknown aggregate type '{aggregateTypeName}'. You may need to register this aggregate type with StoreOptions.Events.AggregateFor<T>()");
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

        IEnumerable<Type> IFeatureSchema.DependentTypes()
        {
            yield break;
        }

        ISchemaObject[] IFeatureSchema.Objects
        {
            get
            {
                var eventsTable = new EventsTable(DatabaseSchemaName);
                var sequence = new Sequence(new DbObjectName(DatabaseSchemaName, "mt_events_sequence"))
                {
                    Owner = eventsTable.Identifier,
                    OwnerColumn = "seq_id"
                };



                return new ISchemaObject[]
                {
                    new StreamsTable(DatabaseSchemaName),
                    eventsTable,
                    new EventProgressionTable(DatabaseSchemaName), 
                    sequence,  
                    new SystemFunction(DatabaseSchemaName, "mt_append_event", "uuid, varchar, uuid[], varchar[], jsonb[]"), 
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
    }

    public class StreamsTable : Table
    {
        public StreamsTable(string schemaName) : base(new DbObjectName(schemaName, "mt_streams"))
        {
            AddPrimaryKey(new TableColumn("id", "uuid"));
            AddColumn("type", "varchar(100)", "NULL");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            AddColumn("snapshot", "jsonb");
            AddColumn("snapshot_version", "integer");
        }
    }

    public class EventsTable : Table
    {
        public EventsTable(string schemaName) : base(new DbObjectName(schemaName, "mt_events"))
        {
            AddPrimaryKey(new TableColumn("seq_id", "bigint"));
            AddColumn("id", "uuid", "NOT NULL");
            AddColumn("stream_id", "uuid", $"REFERENCES {schemaName}.mt_streams ON DELETE CASCADE");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("data", "jsonb", "NOT NULL");
            AddColumn("type", "varchar(100)", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            
            Constraints.Add("CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)");
            Constraints.Add("CONSTRAINT pk_mt_events_id_unique UNIQUE(id)");
        }
    }

    public class EventProgressionTable : Table
    {
        public EventProgressionTable(string schemaName) : base(new DbObjectName(schemaName, "mt_event_progression"))
        {
            AddPrimaryKey(new TableColumn("name", "varchar"));
            AddColumn("last_seq_id", "bigint", "NULL");
        }
    }
}