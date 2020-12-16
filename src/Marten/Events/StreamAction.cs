using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events.V4Concept;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Events
{
    public enum StreamActionType
    {
        /// <summary>
        /// This is a new stream. This action will be rejected
        /// if a stream with the same identity exists in the database
        /// </summary>
        Start,

        /// <summary>
        /// Append these events to an existing stream. If the stream does not
        /// already exist, it will be created with these events
        /// </summary>
        Append
    }

    /// <summary>
    /// Models a series of events to be appended to either a new or
    /// existing stream
    /// </summary>
    public class StreamAction
    {
        /// <summary>
        /// Identity of the stream if using Guid's as the identity
        /// </summary>
        public Guid Id { get; internal set; }

        /// <summary>
        /// The identity of this stream if using strings as the stream
        /// identity
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Is this action the start of a new stream or appending
        /// to an existing stream?
        /// </summary>
        public StreamActionType ActionType { get; }

        /// <summary>
        /// If the stream was started as tagged to an aggregate type, that will
        /// be reflected in this property. May be null
        /// </summary>
        public Type AggregateType { get; internal set; }

        /// <summary>
        /// Marten's name for the aggregate type that will be persisted
        /// to the streams table
        /// </summary>
        public string AggregateTypeName { get; internal set; }

        /// <summary>
        /// The Id of the current tenant
        /// </summary>
        public string TenantId { get; internal set; }



        private readonly List<IEvent> _events = new List<IEvent>();

        private StreamAction(Guid stream, StreamActionType actionType)
        {
            Id = stream;
            ActionType = actionType;
        }

        private StreamAction(string stream, StreamActionType actionType)
        {
            Key = stream;
            ActionType = actionType;
        }

        private StreamAction(Guid id, string key, StreamActionType actionType)
        {
            Id = id;
            Key = key;
            ActionType = actionType;
        }


        internal StreamAction AddEvents(IEnumerable<object> events)
        {
            // TODO -- let's get rid of this maybe?
            _events.AddRange(events.Select(coerce));

            foreach (var @event in _events)
            {
                if (@event.Id == Guid.Empty) @event.Id = CombGuidIdGeneration.NewGuid();
                @event.StreamId = Id;
                @event.StreamKey = Key;
            }

            return this;
        }

        public IReadOnlyList<IEvent> Events => _events;
        public int? ExpectedVersionOnServer { get; internal set; }

        public int Version { get; internal set; }


        public DateTime? Timestamp { get; internal set; }

        public DateTime? Created { get; internal set; }

        /// <summary>
        /// Strictly for testing
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="@event"></param>
        /// <returns></returns>
        internal StreamAction Add<T>(T @event)
        {
            _events.Add(new Event<T>(@event) {
                Id = CombGuidIdGeneration.NewGuid(),
                StreamId = Id,
                StreamKey = Key,

            });
            return this;
        }

        /// <summary>
        /// Create a new StreamAction for starting a new stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        /// <exception cref="EmptyEventStreamException"></exception>
        public static StreamAction Start(Guid streamId, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamId);

            return new StreamAction(streamId, StreamActionType.Start).AddEvents(events);
        }

        /// <summary>
        /// Create a new StreamAction for starting a new stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        /// <exception cref="EmptyEventStreamException"></exception>
        public static StreamAction Start(string streamKey, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamKey);
            return new StreamAction(streamKey, StreamActionType.Start).AddEvents(events);
        }

        /// <summary>
        /// Create a new StreamAction for appending to an existing stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static StreamAction Append(Guid streamId, params object[] events)
        {
            var stream = new StreamAction(streamId, StreamActionType.Append);
            stream.AddEvents(events);
            return stream;
        }

        private static IEvent coerce(object e)
        {
            if (e is IEvent @event) return @event;
            return new Event(e);
        }

        /// <summary>
        /// Create a new StreamAction for appending to an existing stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static StreamAction Append(string streamKey, params object[] events)
        {
            var stream = new StreamAction(streamKey, StreamActionType.Append);
            stream._events.AddRange(events.Select(coerce).OrderBy(x => x.Version));
            return stream;
        }

        /// <summary>
        /// Applies versions, .Net type aliases, the reserved sequence numbers, timestamps, etc.
        /// to get the events ready to be inserted into the mt_events table
        /// </summary>
        /// <param name="currentVersion"></param>
        /// <param name="graph"></param>
        /// <param name="sequences"></param>
        /// <param name="session"></param>
        /// <exception cref="EventStreamUnexpectedMaxEventIdException"></exception>
        internal void PrepareEvents(int currentVersion, EventGraph graph, Queue<long> sequences, IMartenSession session)
        {
            var timestamp = DateTimeOffset.UtcNow;

            if (AggregateType != null)
            {
                AggregateTypeName = graph.AggregateAliasFor(AggregateType);
            }

            var i = currentVersion;

            if (currentVersion != 0)
            {
                // Guard logic for optimistic concurrency
                if (ExpectedVersionOnServer.HasValue)
                {
                    if (currentVersion != ExpectedVersionOnServer.Value)
                    {
                        throw new EventStreamUnexpectedMaxEventIdException((object) Key ?? Id, AggregateType, ExpectedVersionOnServer.Value, currentVersion);
                    }
                }

                ExpectedVersionOnServer = currentVersion;
            }

            foreach (var @event in _events)
            {
                @event.Version = ++i;
                @event.DotNetTypeName = graph.DotnetTypeNameFor(@event.EventType);

                var mapping = graph.EventMappingFor(@event.EventType);
                @event.EventTypeName = mapping.EventTypeName;
                if (@event.Id == Guid.Empty)
                {
                    @event.Id = CombGuidIdGeneration.NewGuid();
                }
                @event.Sequence = sequences.Dequeue();
                @event.TenantId ??= session.Tenant.TenantId;
                @event.Timestamp = timestamp;
            }

            Version = Events.Last().Version;
        }

        [Obsolete("This is temporary")]
        internal StreamAction ShimForOldProjections()
        {
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i] is Event e)
                {
                    _events[i] = e.Clone();
                }
            }
            return this;
        }

        internal static StreamAction ForReference(Guid streamId, ITenant tenant)
        {
            return new StreamAction(streamId, StreamActionType.Append)
            {
                TenantId = tenant?.TenantId
            };
        }

        internal static StreamAction ForReference(string streamKey, ITenant tenant)
        {
            return new StreamAction(streamKey, StreamActionType.Append)
            {
                TenantId = tenant?.TenantId
            };
        }

        internal static StreamAction ForTombstone()
        {
            return new StreamAction(EstablishTombstoneStream.StreamId, EstablishTombstoneStream.StreamKey, StreamActionType.Append)
            {

            };
        }
    }
}
