using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Progress;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events
{
    public enum StreamIdentity
    {
        AsGuid,
        AsString
    }

    public class EventGraph: IFeatureSchema
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

        public bool IsActive(StoreOptions options) => _events.Any() || Projections.Any() ;

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? Options.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public Type AggregateTypeFor(string aggregateTypeName)
        {
            return _aggregateTypeByName[aggregateTypeName];
        }

        internal DbObjectName ProgressionTable => new DbObjectName(DatabaseSchemaName, "mt_event_progression");
        internal DbObjectName StreamsTable => new DbObjectName(DatabaseSchemaName, "mt_streams");

        public string AggregateAliasFor(Type aggregateType)
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

                // SAMPLE: using-sequence
                var sequence = new Sequence(new DbObjectName(DatabaseSchemaName, "mt_events_sequence"))
                {
                    Owner = eventsTable.Identifier,
                    OwnerColumn = "seq_id"
                };
                // ENDSAMPLE

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
                    new DropFunction(DatabaseSchemaName, "mt_append_event", appendEventFunctionArgs),
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

            var stream = StreamAction.Start(this, id, events);
            session.WorkTracker.Streams.Add(stream);

            return stream;
        }

        internal StreamAction StartStream(DocumentSessionBase session, string streamKey, params object[] events)
        {
            EnsureAsStringStorage(session);

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

        public ProjectionCollection Projections { get; }

        public IEvent BuildEvent(object eventData)
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

        /// <summary>
        /// Check the current progress of all asynchronous projections
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<IReadOnlyList<ProjectionProgress>> AllProjectionProgress(CancellationToken token = default(CancellationToken))
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));

            var handler = (IQueryHandler<IReadOnlyList<ProjectionProgress>>)new ListQueryHandler<ProjectionProgress>(new ProjectionProgressStatement(this),
                new ProjectionProgressSelector());

            using (var session = (QuerySession)_store.QuerySession())
            {
                return await session.ExecuteHandlerAsync(handler, token);
            }
        }

        /// <summary>
        /// Check the current progress of a single projection or projection shard
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<long> ProjectionProgressFor(string projectionOrShardName, CancellationToken token = default(CancellationToken))
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));

            var statement = new ProjectionProgressStatement(this)
            {
                ProjectionOrShardName = projectionOrShardName
            };

            var handler = new OneResultHandler<ProjectionProgress>(statement,
                new ProjectionProgressSelector(), true, false);

            await using var session = (QuerySession)_store.QuerySession();

            var progress = await session.ExecuteHandlerAsync(handler, token);

            return progress?.LastSequenceId ?? 0;
        }

        /// <summary>
        /// Fetch the current size of the event store tables, including the current value
        /// of the event sequence number
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<EventStoreStatistics> FetchStatistics(CancellationToken token = default)
        {
            var sql = $@"
select count(*) from {DatabaseSchemaName}.mt_events;
select count(*) from {DatabaseSchemaName}.mt_streams;
select last_value from {DatabaseSchemaName}.mt_events_sequence;
";

            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));

            var statistics = new EventStoreStatistics();

            await using var conn = _store.Tenancy.Default.CreateConnection();
            await conn.OpenAsync(token);

            await using var reader = await conn.CreateCommand(sql).ExecuteReaderAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.EventCount = await reader.GetFieldValueAsync<long>(0, token);
            }

            await reader.NextResultAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.StreamCount = await reader.GetFieldValueAsync<long>(0, token);
            }

            await reader.NextResultAsync(token);

            if (await reader.ReadAsync(token))
            {
                statistics.EventSequenceNumber = await reader.GetFieldValueAsync<long>(0, token);
            }

            return statistics;
        }
    }
}
