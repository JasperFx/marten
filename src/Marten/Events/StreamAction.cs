using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Operations;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Schema.Identity;
using Marten.Storage;
#nullable enable
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
        public string? Key { get; }

        /// <summary>
        /// Is this action the start of a new stream or appending
        /// to an existing stream?
        /// </summary>
        public StreamActionType ActionType { get; }

        /// <summary>
        /// If the stream was started as tagged to an aggregate type, that will
        /// be reflected in this property. May be null
        /// </summary>
        public Type? AggregateType { get; internal set; }

        /// <summary>
        /// Marten's name for the aggregate type that will be persisted
        /// to the streams table
        /// </summary>
        public string? AggregateTypeName { get; internal set; }

        /// <summary>
        /// The Id of the current tenant
        /// </summary>
        public string? TenantId { get; internal set; }



        private readonly List<IEvent> _events = new();

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

        internal StreamAction AddEvents(IReadOnlyList<IEvent> events)
        {
            _events.AddRange(events);

            foreach (var @event in events)
            {
                if (@event.Id == Guid.Empty) @event.Id = CombGuidIdGeneration.NewGuid();
                @event.StreamId = Id;
                @event.StreamKey = Key;

                if (ExpectedVersionOnServer.HasValue)
                {
                    ExpectedVersionOnServer++;
                }
            }

            return this;
        }

        /// <summary>
        /// The events involved in this action
        /// </summary>
        public IReadOnlyList<IEvent> Events => _events;

        /// <summary>
        /// The expected starting version of the stream in the server. This is used
        /// to facilitate optimistic concurrency checks
        /// </summary>
        public long? ExpectedVersionOnServer { get; internal set; }

        /// <summary>
        /// The ending version of the stream for this action
        /// </summary>
        public long Version { get; internal set; }

        /// <summary>
        /// The recorded timestamp for these events
        /// </summary>
        public DateTimeOffset? Timestamp { get; internal set; }

        /// <summary>
        /// When was the stream created
        /// </summary>
        public DateTimeOffset? Created { get; internal set; }


        /// <summary>
        /// Create a new StreamAction for starting a new stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        /// <exception cref="EmptyEventStreamException"></exception>
        public static StreamAction Start(EventGraph graph, Guid streamId, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamId);

            return new StreamAction(streamId, StreamActionType.Start).AddEvents(events.Select(graph.BuildEvent).ToArray());
        }

        /// <summary>
        /// Create a new StreamAction for starting a new stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        /// <exception cref="EmptyEventStreamException"></exception>
        public static StreamAction Start(Guid streamId, params IEvent[] events)
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
        public static StreamAction Start(EventGraph graph, string streamKey, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamKey);
            return new StreamAction(streamKey, StreamActionType.Start).AddEvents(events.Select(graph.BuildEvent).ToArray());
        }

        /// <summary>
        /// Create a new StreamAction for starting a new stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        /// <exception cref="EmptyEventStreamException"></exception>
        public static StreamAction Start(string streamKey, params IEvent[] events)
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
        public static StreamAction Append(EventGraph graph, Guid streamId, params object[] events)
        {
            var stream = new StreamAction(streamId, StreamActionType.Append);
            stream.AddEvents(events.Select(graph.BuildEvent).ToArray());
            return stream;
        }

        /// <summary>
        /// Create a new StreamAction for appending to an existing stream
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static StreamAction Append(Guid streamId, IEvent[] events)
        {
            var stream = new StreamAction(streamId, StreamActionType.Append);
            stream.AddEvents(events);
            return stream;
        }

        /// <summary>
        /// Create a new StreamAction for appending to an existing stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static StreamAction Append(EventGraph graph, string streamKey, params object[] events)
        {
            var stream = new StreamAction(streamKey, StreamActionType.Append);
            stream._events.AddRange(events.Select(graph.BuildEvent));
            return stream;
        }

        /// <summary>
        /// Create a new StreamAction for appending to an existing stream
        /// </summary>
        /// <param name="streamKey"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static StreamAction Append(string streamKey, IEvent[] events)
        {
            var stream = new StreamAction(streamKey, StreamActionType.Append);
            stream._events.AddRange(events.OrderBy(x => x.Version));
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
        internal void PrepareEvents(long currentVersion, EventGraph graph, Queue<long> sequences, IMartenSession session)
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
                        throw new EventStreamUnexpectedMaxEventIdException((object?) Key ?? Id, AggregateType, ExpectedVersionOnServer.Value, currentVersion);
                    }
                }

                ExpectedVersionOnServer = currentVersion;
            }

            foreach (var @event in _events)
            {
                @event.Version = ++i;
                if (@event.Id == Guid.Empty)
                {
                    @event.Id = CombGuidIdGeneration.NewGuid();
                }
                @event.Sequence = sequences.Dequeue();
                @event.TenantId = session.Tenant.TenantId;
                @event.Timestamp = timestamp;

                ProcessMetadata(@event, graph, session);
            }

            Version = Events.Last().Version;
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

        private static void ProcessMetadata(IEvent @event, EventGraph graph, IMartenSession session)
        {
            if (graph.Metadata.CausationId.Enabled)
            {
                @event.CausationId ??= session.CausationId;
            }

            if (graph.Metadata.CorrelationId.Enabled)
            {
                @event.CorrelationId = session.CorrelationId;
            }

            if (!graph.Metadata.Headers.Enabled) return;
            if (!(session.Headers?.Count > 0)) return;
            foreach (var header in session.Headers)
            {
                @event.SetHeader(header.Key, header.Value);
            }
        }

        internal static StreamAction For(Guid streamId, IReadOnlyList<IEvent> events)
        {
            var action = events[0].Version == 1 ? StreamActionType.Start : StreamActionType.Append;
            return new StreamAction(streamId, action)
                .AddEvents(events);
        }

        internal static StreamAction For(string streamKey, IReadOnlyList<IEvent> events)
        {
            var action = events[0].Version == 1 ? StreamActionType.Start : StreamActionType.Append;
            return new StreamAction(streamKey, action)
                .AddEvents(events);
        }
    }
}
