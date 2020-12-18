using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Events.V4Concept;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using IProjection = Marten.Events.Projections.IProjection;

namespace Marten.Events
{
    public enum StreamIdentity
    {
        AsGuid,
        AsString
    }

    public class EventGraph: IFeatureSchema
    {
        private readonly Ref<ImHashMap<string, IAggregator>> _aggregateByName =
            Ref.Of(ImHashMap<string, IAggregator>.Empty);

        private readonly Ref<ImHashMap<Type, IAggregator>> _aggregates =
            Ref.Of(ImHashMap<Type, IAggregator>.Empty);

        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        private IAggregatorLookup _aggregatorLookup;
        private string _databaseSchemaName;

        private readonly Lazy<IInlineProjection[]> _inlineProjections;

        private readonly Lazy<EstablishTombstoneStream> _establishTombstone;


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

            // TODO -- this will change when we switch all the way over to the new V4 model
            _inlineProjections = new Lazy<IInlineProjection[]>(() => InlineProjections.Select(x => new TemporaryV4InlineShim(x)).OfType<IInlineProjection>().ToArray());

            _establishTombstone = new Lazy<EstablishTombstoneStream>(() => new EstablishTombstoneStream(this));

            V4Projections = new V4ProjectionCollection(options);
        }

        public StreamIdentity StreamIdentity { get; set; } = StreamIdentity.AsGuid;

        public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

        /// <summary>
        ///     Whether a "for update" (row exclusive lock) should be used when selecting out the event version to use from the streams table
        /// </summary>
        /// <remkarks>
        ///     Not using this can result in race conditions in a concurrent environment that lead to
        ///       event version mismatches between the event and stream version numbers
        /// </remkarks>
        public bool UseAppendEventForUpdateLock { get; set; } = false;

        internal StoreOptions Options { get; }

        internal DbObjectName Table => new DbObjectName(DatabaseSchemaName, "mt_events");

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : class
        {
            return EventMappingFor(typeof(T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }

        public IEnumerable<IAggregator> AllAggregates()
        {
            return _aggregates.Value.Enumerate().Select(x => x.Value);
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

        public bool IsActive(StoreOptions options) => _events.Any() || _aggregates.Value.Enumerate().Any();

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public void AddAggregator<T>(IAggregator<T> aggregator) where T : class
        {
            Options.Storage.MappingFor(typeof(T));
            _aggregates.Swap(a => a.AddOrUpdate(typeof(T), aggregator));
        }

        public IAggregator<T> AggregateFor<T>() where T : class
        {
            if (!_aggregates.Value.TryFind(typeof(T), out var aggregator))
            {
                Options.Storage.MappingFor(typeof(T));
                aggregator = _aggregatorLookup.Lookup<T>();
                _aggregates.Swap(a => a.AddOrUpdate(typeof(T), aggregator));
            }
            return aggregator.As<IAggregator<T>>();
        }

        public Type AggregateTypeFor(string aggregateTypeName)
        {
            if (_aggregateByName.Value.TryFind(aggregateTypeName, out var aggregate))
            {
                return aggregate.AggregateType;
            }

            aggregate = AllAggregates().FirstOrDefault(x => x.Alias == aggregateTypeName);
            if (aggregate == null)
            {
                return null;
            }

            _aggregateByName.Swap(a => a.AddOrUpdate(aggregateTypeName, aggregate));

            return aggregate.AggregateType;
        }

        [Obsolete("Remove in V4")]
        public ProjectionCollection InlineProjections { get; }

        [Obsolete("Remove in V4")]
        public ProjectionCollection AsyncProjections { get; }
        internal DbObjectName ProgressionTable => new DbObjectName(DatabaseSchemaName, "mt_event_progression");

        public string AggregateAliasFor(Type aggregateType)
        {
            if (!_aggregates.Value.TryFind(aggregateType, out var aggregator))
            {
                aggregator = _aggregatorLookup.Lookup(aggregateType);
                _aggregates.Swap(a => a.AddOrUpdate(aggregateType, aggregator));
            }

            return aggregator.Alias;
        }

        public IProjection ProjectionFor(Type viewType)
        {
            return AsyncProjections.ForView(viewType) ?? InlineProjections.ForView(viewType);
        }

        public ViewProjection<TView, TId> ProjectView<TView, TId>() where TView : class
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

        void IFeatureSchema.WritePermissions(DdlRules rules, StringWriter writer)
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

        private readonly Ref<ImHashMap<Type, string>> _dotnetTypeNames = Ref.Of(ImHashMap<Type, string>.Empty);

        internal string DotnetTypeNameFor(Type type)
        {
            if (!_dotnetTypeNames.Value.TryFind(type, out var value))
            {
                value = $"{type.FullName}, {type.Assembly.GetName().Name}";

                _dotnetTypeNames.Swap(d => d.AddOrUpdate(type, value));
            }

            return value;
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

            if (session.UnitOfWork.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(events);
            }
            else
            {
                eventStream = StreamAction.Append(stream, events);
                session.UnitOfWork.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction Append(DocumentSessionBase session, string stream, params object[] events)
        {
            EnsureAsStringStorage(session);

            if (session.UnitOfWork.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(events);
            }
            else
            {
                eventStream = StreamAction.Append(stream, events);
                session.UnitOfWork.Streams.Add(eventStream);
            }

            return eventStream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, Guid id, params object[] events)
        {
            EnsureAsGuidStorage(session);

            var stream = StreamAction.Start(id, events);
            session.UnitOfWork.Streams.Add(stream);

            return stream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, string streamKey, params object[] events)
        {
            EnsureAsStringStorage(session);

            var stream = StreamAction.Start(streamKey, events);

            session.UnitOfWork.Streams.Add(stream);

            return stream;
        }

        internal void ProcessEvents(DocumentSessionBase session)
        {
            if (!session.UnitOfWork.Streams.Any())
            {
                return;
            }

            var storage = session.EventStorage();

            // TODO -- we'll optimize this later to batch up queries to the database
            var fetcher = new EventSequenceFetcher(this, session.UnitOfWork.Streams.Sum(x => x.Events.Count));
            var sequences = session.ExecuteHandler(fetcher);


            foreach (var stream in session.UnitOfWork.Streams)
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
                projection.Apply(session, session.UnitOfWork.Streams.ToList());
            }
        }

        internal async Task ProcessEventsAsync(DocumentSessionBase session, CancellationToken token)
        {
            if (!session._unitOfWork.Streams.Any())
            {
                return;
            }

            // TODO -- we'll optimize this later to batch up queries to the database
            var fetcher = new EventSequenceFetcher(this, session.UnitOfWork.Streams.Sum(x => x.Events.Count));
            var sequences = await session.ExecuteHandlerAsync(fetcher, token);


            var storage = session.EventStorage();

            foreach (var stream in session.UnitOfWork.Streams)
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
                await projection.ApplyAsync(session, session.UnitOfWork.Streams.ToList(), token);
            }
        }

        internal bool TryCreateTombstoneBatch(DocumentSessionBase session, out UpdateBatch batch)
        {
            if (session.UnitOfWork.Streams.Any())
            {
                var stream = StreamAction.ForTombstone();

                var tombstone = new Tombstone();
                var mapping = EventMappingFor<Tombstone>();

                var operations = new List<IStorageOperation>();
                var storage = session.EventStorage();

                var dotNetTypeName = DotnetTypeNameFor(typeof(Tombstone));

                operations.Add(_establishTombstone.Value);
                var tombstones = session.UnitOfWork.Streams
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
                        DotNetTypeName = dotNetTypeName
                    })
                    .Select(e => storage.AppendEvent(this, session, stream, e));

                operations.AddRange(tombstones);

                batch = new UpdateBatch(operations);

                return true;
            }

            batch = null;
            return false;
        }

        public V4ProjectionCollection V4Projections { get; }
    }
}
