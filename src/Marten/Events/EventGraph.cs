using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Baseline.ImTools;
using Marten.Events.Daemon;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events
{
    public partial class EventGraph: IEventStoreOptions, IReadOnlyEventStoreOptions
    {
        private readonly Cache<Type, string> _aggregateNameByType =
            new(type => type.Name.ToTableAlias());

        private readonly Cache<string, Type> _aggregateTypeByName;

        private readonly Cache<string, EventMapping> _byEventName = new();

        private readonly Lazy<EstablishTombstoneStream> _establishTombstone;
        private readonly Cache<Type, EventMapping> _events = new();

        private readonly Lazy<IProjection[]> _inlineProjections;


        private readonly Ref<ImHashMap<string, Type>> _nameToType = Ref.Of(ImHashMap<string, Type>.Empty);

        private string _databaseSchemaName;

        private DocumentStore _store;
        private StreamIdentity _streamIdentity = StreamIdentity.AsGuid;

        internal EventGraph(StoreOptions options)
        {
            StreamIdentity = StreamIdentity.AsGuid;
            Options = options;
            _events.OnMissing = eventType =>
            {
                var mapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
                Options.Storage.AddMapping(mapping);

                return mapping;
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };

            _inlineProjections = new Lazy<IProjection[]>(() => options.Projections.BuildInlineProjections(_store));

            _establishTombstone = new Lazy<EstablishTombstoneStream>(() => new EstablishTombstoneStream(this));

            _aggregateTypeByName = new Cache<string, Type>(name => findAggregateType(name));
        }

        internal NpgsqlDbType StreamIdDbType { get; private set; }

        /// <summary>
        ///     Whether a "for update" (row exclusive lock) should be used when selecting out the event version to use from the
        ///     streams table
        /// </summary>
        /// <remarks>
        ///     Not using this can result in race conditions in a concurrent environment that lead to
        ///     event version mismatches between the event and stream version numbers
        /// </remarks>
        [Obsolete("This is no longer used!")]
        public bool UseAppendEventForUpdateLock { get; set; } = false;

        internal StoreOptions Options { get; }

        internal DbObjectName Table => new(DatabaseSchemaName, "mt_events");

        internal EventMetadataCollection Metadata { get; } = new();

        /// <summary>
        ///     Configure whether event streams are identified with Guid or strings
        /// </summary>
        public StreamIdentity StreamIdentity
        {
            get => _streamIdentity;
            set
            {
                _streamIdentity = value;
                StreamIdDbType = value == StreamIdentity.AsGuid ? NpgsqlDbType.Uuid : NpgsqlDbType.Varchar;
            }
        }

        /// <summary>
        ///     Configure the event sourcing storage for multi-tenancy
        /// </summary>
        public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

        /// <summary>
        ///     Configure the meta data required to be stored for events. By default meta data fields are disabled
        /// </summary>
        public MetadataConfig MetadataConfig => new(Metadata);

        /// <summary>
        ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
        ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
        ///     the event type
        /// </summary>
        /// <param name="eventType"></param>
        public void AddEventType(Type eventType)
        {
            _events.FillDefault(eventType);
        }

        /// <summary>
        ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
        ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
        ///     the event type
        /// </summary>
        /// <param name="types"></param>
        public void AddEventTypes(IEnumerable<Type> types)
        {
            types.Each(AddEventType);
        }

        /// <summary>
        ///     Override the database schema name for event related tables. By default this
        ///     is the same schema as the document storage
        /// </summary>
        public string DatabaseSchemaName
        {
            get => _databaseSchemaName ?? Options.DatabaseSchemaName;
            set => _databaseSchemaName = value;
        }

        IReadOnlyDaemonSettings IReadOnlyEventStoreOptions.Daemon => _store.Options.Projections;

        IReadOnlyList<IProjectionSource> IReadOnlyEventStoreOptions.Projections()
        {
            return Options.Projections.All.OfType<IProjectionSource>().ToList();
        }

        public IReadOnlyList<IEventType> AllKnownEventTypes()
        {
            return _events.OfType<IEventType>().ToList();
        }

        IReadonlyMetadataConfig IReadOnlyEventStoreOptions.MetadataConfig => MetadataConfig;

        private Type findAggregateType(string name)
        {
            foreach (var aggregateType in Options.Projections.AllAggregateTypes())
            {
                var possibleName = _aggregateNameByType[aggregateType];
                if (name.EqualsIgnoreCase(possibleName))
                {
                    return aggregateType;
                }
            }

            return null;
        }

        internal EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        internal EventMapping EventMappingFor<T>() where T : class
        {
            return EventMappingFor(typeof(T));
        }

        internal IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }

        internal EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }

        internal bool IsActive(StoreOptions options)
        {
            return _events.Any() || Options.Projections.All.Any();
        }

        internal Type AggregateTypeFor(string aggregateTypeName)
        {
            return _aggregateTypeByName[aggregateTypeName];
        }

        internal string AggregateAliasFor(Type aggregateType)
        {
            var alias = _aggregateNameByType[aggregateType];

            _aggregateTypeByName.Fill(alias, aggregateType);

            return alias;
        }


        internal string GetStreamIdDBType()
        {
            return StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
        }

        internal Type GetStreamIdType()
        {
            return StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        }

        internal Type TypeForDotNetName(string assemblyQualifiedName)
        {
            if (!_nameToType.Value.TryFind(assemblyQualifiedName, out var value))
            {
                value = Type.GetType(assemblyQualifiedName);
                if (value == null)
                {
                    throw new UnknownEventTypeException($"Unable to load event type '{assemblyQualifiedName}'.");
                }

                _nameToType.Swap(n => n.AddOrUpdate(assemblyQualifiedName, value));
            }

            return value;
        }

        internal IEventStorage EnsureAsStringStorage(IMartenSession session)
        {
            if (StreamIdentity == StreamIdentity.AsGuid)
            {
                throw new InvalidOperationException(
                    "This Marten event store is configured to identify streams with Guids");
            }

            return session.EventStorage();
        }

        internal IEventStorage EnsureAsGuidStorage(IMartenSession session)
        {
            if (StreamIdentity == StreamIdentity.AsString)
            {
                throw new InvalidOperationException(
                    "This Marten event store is configured to identify streams with strings");
            }

            return session.EventStorage();
        }


        internal IEvent BuildEvent(object eventData)
        {
            if (eventData == null)
            {
                throw new ArgumentNullException(nameof(eventData));
            }

            var mapping = EventMappingFor(eventData.GetType());
            return mapping.Wrap(eventData);
        }

        internal void AssertValidity(DocumentStore store)
        {
            _store = store;
        }
    }
}
