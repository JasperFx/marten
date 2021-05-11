using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.ImTools;
using Marten.Events.Archiving;
using Marten.Events.Daemon;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Weasel.Postgresql;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Util;
using Weasel.Postgresql.Functions;

namespace Marten.Events
{
    public class EventGraph: IFeatureSchema, IEventStoreOptions, IReadOnlyEventStoreOptions
    {

        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        private readonly Cache<string, Type> _aggregateTypeByName;

        private readonly Cache<Type, string> _aggregateNameByType =
            new Cache<Type, string>(type => type.Name.ToTableAlias());

        private string _databaseSchemaName;

        private readonly Lazy<IProjection[]> _inlineProjections;

        private readonly Lazy<EstablishTombstoneStream> _establishTombstone;

        private DocumentStore _store;

        public EventGraph(StoreOptions options)
        {
            Options = options;
            _events.OnMissing = eventType =>
            {
                var mapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
                Options.Storage.AddMapping(mapping);

                return mapping;
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };

            _inlineProjections = new Lazy<IProjection[]>(() => Projections.BuildInlineProjections(_store));

            _establishTombstone = new Lazy<EstablishTombstoneStream>(() => new EstablishTombstoneStream(this));

            Projections = new ProjectionCollection(options);

            _aggregateTypeByName = new Cache<string, Type>(name => findAggregateType(name));
        }

        IReadOnlyDaemonSettings IReadOnlyEventStoreOptions.Daemon => Daemon;

        IReadOnlyList<IProjectionSource> IReadOnlyEventStoreOptions.Projections()
        {
            return Projections.Projections.OfType<IProjectionSource>().ToList();
        }

        public IReadOnlyList<IEventType> AllKnownEventTypes()
        {
            return _events.OfType<IEventType>().ToList();
        }

        IReadonlyMetadataConfig IReadOnlyEventStoreOptions.MetadataConfig => MetadataConfig;

        private Type findAggregateType(string name)
        {
            foreach (var aggregateType in Projections.AllAggregateTypes())
            {
                var possibleName = _aggregateNameByType[aggregateType];
                if (name.EqualsIgnoreCase(possibleName)) return aggregateType;
            }

            return null;
        }

        /// <summary>
        /// Advanced configuration for the asynchronous projection execution
        /// </summary>
        public DaemonSettings Daemon { get; } = new DaemonSettings();

        /// <summary>
        /// Configure whether event streams are identified with Guid or strings
        /// </summary>
        public StreamIdentity StreamIdentity { get; set; } = StreamIdentity.AsGuid;

        /// <summary>
        /// Configure the event sourcing storage for multi-tenancy
        /// </summary>
        public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

        /// <summary>
        ///     Whether a "for update" (row exclusive lock) should be used when selecting out the event version to use from the streams table
        /// </summary>
        /// <remarks>
        ///     Not using this can result in race conditions in a concurrent environment that lead to
        ///       event version mismatches between the event and stream version numbers
        /// </remarks>
        [Obsolete("This is no longer used!")]
        public bool UseAppendEventForUpdateLock { get; set; } = false;

        /// <summary>
        /// Configure the meta data required to be stored for events. By default meta data fields are disabled
        /// </summary>
        public MetadataConfig MetadataConfig => new(Metadata);

        internal StoreOptions Options { get; }

        internal DbObjectName Table => new DbObjectName(DatabaseSchemaName, "mt_events");

        internal EventMetadataCollection Metadata { get; } = new();

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

        /// <summary>
        /// Register an event type with Marten. This isn't strictly necessary for normal usage,
        /// but can help Marten with asynchronous projections where Marten hasn't yet encountered
        /// the event type
        /// </summary>
        /// <param name="eventType"></param>
        public void AddEventType(Type eventType)
        {
            _events.FillDefault(eventType);
        }

        /// <summary>
        /// Register an event type with Marten. This isn't strictly necessary for normal usage,
        /// but can help Marten with asynchronous projections where Marten hasn't yet encountered
        /// the event type
        /// </summary>
        /// <param name="types"></param>
        public void AddEventTypes(IEnumerable<Type> types)
        {
            types.Each(AddEventType);
        }

        internal bool IsActive(StoreOptions options) => _events.Any() || Projections.Any() ;

        /// <summary>
        /// Override the database schema name for event related tables. By default this
        /// is the same schema as the document storage
        /// </summary>
        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        internal Type AggregateTypeFor(string aggregateTypeName)
        {
            return _aggregateTypeByName[aggregateTypeName];
        }

        internal DbObjectName ProgressionTable => new DbObjectName(DatabaseSchemaName, "mt_event_progression");
        internal DbObjectName StreamsTable => new DbObjectName(DatabaseSchemaName, "mt_streams");

        internal string AggregateAliasFor(Type aggregateType)
        {
            var alias = _aggregateNameByType[aggregateType];

            _aggregateTypeByName.Fill(alias, aggregateType);

            return alias;
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
                var streamsTable = new StreamsTable(this);

                #region sample_using-sequence
                var sequence = new Sequence(new DbObjectName(DatabaseSchemaName, "mt_events_sequence"))
                {
                    Owner = eventsTable.Identifier,
                    OwnerColumn = "seq_id"
                };
                #endregion sample_using-sequence

                // compute the args for mt_append_event function
                var streamIdTypeArg = StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
                var appendEventFunctionArgs = $"{streamIdTypeArg}, varchar, varchar, uuid[], varchar[], varchar[], jsonb[]";

                return new ISchemaObject[]
                {
                    streamsTable,
                    eventsTable,
                    new EventProgressionTable(DatabaseSchemaName),
                    sequence,
                    new SystemFunction(DatabaseSchemaName, "mt_mark_event_progression", "varchar, bigint"),
                    Function.ForRemoval(new DbObjectName(DatabaseSchemaName, "mt_append_event")),
                    new ArchiveStreamFunction(this)
                };
            }
        }

        Type IFeatureSchema.StorageType => typeof(EventGraph);
        string IFeatureSchema.Identifier { get; } = "eventstore";

        void IFeatureSchema.WritePermissions(DdlRules rules, TextWriter writer)
        {
            // Nothing
        }

        internal string GetStreamIdDBType()
        {
            return StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
        }

        internal Type GetStreamIdType()
        {
            return StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        }


        private readonly Ref<ImHashMap<string, Type>> _nameToType = Ref.Of(ImHashMap<string, Type>.Empty);

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
                throw new InvalidOperationException("This Marten event store is configured to identify streams with Guids");
            return session.EventStorage();
        }

        internal IEventStorage EnsureAsGuidStorage(IMartenSession session)
        {
            if (StreamIdentity == StreamIdentity.AsString)
                throw new InvalidOperationException("This Marten event store is configured to identify streams with strings");
            return session.EventStorage();
        }

        internal StreamAction Append(DocumentSessionBase session, Guid stream, params object[] events)
        {
            EnsureAsGuidStorage(session);

            if (stream == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(stream), "Cannot use an empty Guid as the stream id");

            var wrapped = events.Select(BuildEvent).ToArray();

            if (session.WorkTracker.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(wrapped);
            }
            else
            {
                eventStream = StreamAction.Append(stream, wrapped);
                session.WorkTracker.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction Append(DocumentSessionBase session, string stream, params object[] events)
        {
            EnsureAsStringStorage(session);

            if (stream.IsEmpty())
                throw new ArgumentOutOfRangeException(nameof(stream), "The stream key cannot be null or empty");

            var wrapped = events.Select(BuildEvent).ToArray();

            if (session.WorkTracker.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(wrapped);
            }
            else
            {
                eventStream = StreamAction.Append(stream, wrapped);
                session.WorkTracker.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, Guid id, params object[] events)
        {
            EnsureAsGuidStorage(session);

            if (id == Guid.Empty)
                throw new ArgumentOutOfRangeException(nameof(id), "Cannot use an empty Guid as the stream id");


            var stream = StreamAction.Start(this, id, events);
            session.WorkTracker.Streams.Add(stream);

            return stream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, string streamKey, params object[] events)
        {
            EnsureAsStringStorage(session);

            if (streamKey.IsEmpty())
                throw new ArgumentOutOfRangeException(nameof(streamKey), "The stream key cannot be null or empty");


            var stream = StreamAction.Start(this, streamKey, events);

            session.WorkTracker.Streams.Add(stream);

            return stream;
        }

        internal void ProcessEvents(DocumentSessionBase session)
        {
            if (!session.WorkTracker.Streams.Any())
            {
                return;
            }

            var storage = session.EventStorage();

            // TODO -- we'll optimize this later to batch up queries to the database
            var fetcher = new EventSequenceFetcher(this, session.WorkTracker.Streams.Sum(x => x.Events.Count));
            var sequences = session.ExecuteHandler(fetcher);


            foreach (var stream in session.WorkTracker.Streams)
            {
                stream.TenantId ??= session.Tenant.TenantId;

                if (stream.ActionType == StreamActionType.Start)
                {
                    stream.PrepareEvents(0, this, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    var handler = storage.QueryForStream(stream);
                    var state = session.ExecuteHandler(handler);

                    if (state == null)
                    {
                        stream.PrepareEvents(0, this, sequences, session);
                        session.QueueOperation(storage.InsertStream(stream));
                    }
                    else
                    {
                        stream.PrepareEvents(state.Version, this, sequences, session);
                        session.QueueOperation(storage.UpdateStreamVersion(stream));
                    }
                }

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.AppendEvent(this, session, stream, @event));
                }
            }

            foreach (var projection in _inlineProjections.Value)
            {
                projection.Apply(session, session.WorkTracker.Streams.ToList());
            }
        }

        internal async Task ProcessEventsAsync(DocumentSessionBase session, CancellationToken token)
        {
            if (!session._workTracker.Streams.Any())
            {
                return;
            }

            // TODO -- we'll optimize this later to batch up queries to the database
            var fetcher = new EventSequenceFetcher(this, session.WorkTracker.Streams.Sum(x => x.Events.Count));
            var sequences = await session.ExecuteHandlerAsync(fetcher, token);


            var storage = session.EventStorage();

            foreach (var stream in session.WorkTracker.Streams)
            {
                stream.TenantId ??= session.Tenant.TenantId;

                if (stream.ActionType == StreamActionType.Start)
                {
                    stream.PrepareEvents(0, this, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    var handler = storage.QueryForStream(stream);
                    var state = await session.ExecuteHandlerAsync(handler, token);

                    if (state == null)
                    {
                        stream.PrepareEvents(0, this, sequences, session);
                        session.QueueOperation(storage.InsertStream(stream));
                    }
                    else
                    {
                        stream.PrepareEvents(state.Version, this, sequences, session);
                        session.QueueOperation(storage.UpdateStreamVersion(stream));
                    }
                }

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.AppendEvent(this, session, stream, @event));
                }
            }

            foreach (var projection in _inlineProjections.Value)
            {
                await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token);
            }
        }

        internal bool TryCreateTombstoneBatch(DocumentSessionBase session, out UpdateBatch batch)
        {
            if (session.WorkTracker.Streams.Any())
            {
                var stream = StreamAction.ForTombstone();

                var tombstone = new Tombstone();
                var mapping = EventMappingFor<Tombstone>();

                var operations = new List<IStorageOperation>();
                var storage = session.EventStorage();

                operations.Add(_establishTombstone.Value);
                var tombstones = session.WorkTracker.Streams
                    .SelectMany(x => x.Events)
                    .Select(x => new Event<Tombstone>(tombstone)
                    {
                        Sequence = x.Sequence,
                        Version = x.Version,
                        TenantId = x.TenantId,
                        StreamId = EstablishTombstoneStream.StreamId,
                        StreamKey = EstablishTombstoneStream.StreamKey,
                        Id = CombGuidIdGeneration.NewGuid(),
                        EventTypeName = mapping.EventTypeName,
                        DotNetTypeName = mapping.DotNetTypeName
                    })
                    .Select(e => storage.AppendEvent(this, session, stream, e));

                operations.AddRange(tombstones);

                batch = new UpdateBatch(operations);

                return true;
            }

            batch = null;
            return false;
        }

        /// <summary>
        /// Configuration for all event store projections
        /// </summary>
        public ProjectionCollection Projections { get; }

        internal IEvent BuildEvent(object eventData)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));

            var mapping = EventMappingFor(eventData.GetType());
            return mapping.Wrap(eventData);
        }

        internal void AssertValidity(DocumentStore store)
        {
            _store = store;
            Projections.AssertValidity(_store);
        }
    }
}
