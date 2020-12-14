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
        Start,
        Append
    }

    public class StreamAction
    {
        public Guid Id { get; }

        public string Key { get; }

        public StreamActionType ActionType { get; }

        public Type AggregateType { get; set; }

        public string AggregateTypeName { get; set; }

        public string TenantId { get; set; }



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


        public StreamAction AddEvents(IEnumerable<object> events)
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

        public object Identifier => (object)Key ?? Id;

        public IReadOnlyList<IEvent> Events => _events;
        public int? ExpectedVersionOnServer { get; set; }

        // TODO -- have this set in the StreamAction constructor
        public int Version { get; set; }

        public DateTime? Timestamp { get; set; }

        public DateTime? Created { get; set; }

        /// <summary>
        /// Strictly for testing
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="@event"></param>
        /// <returns></returns>
        public StreamAction Add<T>(T @event)
        {
            _events.Add(new Event<T>(@event) {
                Id = CombGuidIdGeneration.NewGuid(),
                StreamId = Id,
                StreamKey = Key,

            });
            return this;
        }


        public static StreamAction Start(Guid streamId, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamId);

            return new StreamAction(streamId, StreamActionType.Start).AddEvents(events);
        }

        public static StreamAction Start(string streamKey, params object[] events)
        {
            if (!events.Any()) throw new EmptyEventStreamException(streamKey);
            return new StreamAction(streamKey, StreamActionType.Start).AddEvents(events);
        }

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

        public static StreamAction Append(string streamKey, params object[] events)
        {
            var stream = new StreamAction(streamKey, StreamActionType.Append);
            stream._events.AddRange(events.Select(coerce).OrderBy(x => x.Version));
            return stream;
        }

        public void PrepareEvents(int currentVersion, EventGraph graph, Queue<long> sequences, IMartenSession session)
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

        public StreamAction ShimForOldProjections()
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

        public static StreamAction ForReference(Guid streamId, ITenant tenant)
        {
            return new StreamAction(streamId, StreamActionType.Append)
            {
                TenantId = tenant?.TenantId
            };
        }

        public static StreamAction ForReference(string streamKey, ITenant tenant)
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
