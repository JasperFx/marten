using System;
using System.Collections.Generic;
using Marten.Storage;

#nullable enable
namespace Marten.Events
{
    #region sample_IEvent
    public interface IEvent
    {
        /// <summary>
        /// Unique identifier for the event. Uses a sequential Guid
        /// </summary>
        Guid Id { get; set; }

        /// <summary>
        /// The version of the stream this event reflects. The place in the stream.
        /// </summary>
        long Version { get; set; }

        /// <summary>
        /// The sequential order of this event in the entire event store
        /// </summary>
        long Sequence { get; set; }

        /// <summary>
        ///     The actual event data body
        /// </summary>
        object Data { get; }

        /// <summary>
        ///     If using Guid's for the stream identity, this will
        ///     refer to the Stream's Id, otherwise it will always be Guid.Empty
        /// </summary>
        Guid StreamId { get; set; }

        /// <summary>
        ///     If using strings as the stream identifier, this will refer
        ///     to the containing Stream's Id
        /// </summary>
        string? StreamKey { get; set; }

        /// <summary>
        ///     The UTC time that this event was originally captured
        /// </summary>
        DateTimeOffset Timestamp { get; set; }

        /// <summary>
        ///     If using multi-tenancy by tenant id
        /// </summary>
        string TenantId { get; set; }

        /// <summary>
        /// The .Net type of the event body
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// Marten's type alias string for the Event type
        /// </summary>
        string EventTypeName { get; set; }

        /// <summary>
        /// Marten's string representation of the event type
        /// in assembly qualified name
        /// </summary>
        string DotNetTypeName { get; set; }

        /// <summary>
        /// Optional metadata describing the causation id
        /// </summary>
        string? CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id
        /// </summary>
        string? CorrelationId { get; set; }

        /// <summary>
        /// Optional user defined metadata values. This may be null.
        /// </summary>
        Dictionary<string, object>? Headers { get; set; }

        /// <summary>
        /// Set an optional user defined metadata value by key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        void SetHeader(string key, object value);

        /// <summary>
        /// Get an optional user defined metadata value by key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        object? GetHeader(string key);

        /// <summary>
        /// Has this event been archived and no longer applicable
        /// to projected views
        /// </summary>
        bool IsArchived { get; set; }
    }

    #endregion

    public interface IEvent<out T> : IEvent where T : notnull
    {
        new T Data { get; }
    }

    internal class Event<T>: IEvent<T> where T: notnull
    {
        public Event(T data)
        {
            Data = data;
        }

        /// <summary>
        ///     The actual event data
        /// </summary>
        public T Data { get; set; }

        #region sample_event_metadata
        /// <summary>
        ///     A reference to the stream that contains
        ///     this event
        /// </summary>
        public Guid StreamId { get; set; }

        /// <summary>
        ///     A reference to the stream if the stream
        ///     identifier mode is AsString
        /// </summary>
        public string? StreamKey { get; set; }

        /// <summary>
        ///     An alternative Guid identifier to identify
        ///     events across databases
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     An event's version position within its event stream
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        ///     A global sequential number identifying the Event
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        ///     The UTC time that this event was originally captured
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        public string TenantId { get; set; } = Tenancy.DefaultTenantId;

        /// <summary>
        /// Optional metadata describing the causation id
        /// </summary>
        public string? CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// This is meant to be lazy created, and can be null
        /// </summary>
        public Dictionary<string, object>? Headers { get; set; }
        #endregion

        object IEvent.Data => Data;

        public Type EventType => typeof(T);
        public string EventTypeName { get; set; } = null!;
        public string DotNetTypeName { get; set; } = null!;

        public void SetHeader(string key, object value)
        {
            Headers ??= new Dictionary<string, object>();
            Headers[key] = value;
        }

        public object? GetHeader(string key)
        {
            return Headers?[key];
        }

        public bool IsArchived { get; set; }

        protected bool Equals(Event<T> other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != GetType())
                return false;
            return Equals((Event<T>)obj);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }

    public static class EventExtensions
    {
        public static IEvent<T> WithData<T>(this IEvent @event, T eventData) where T: notnull
        {
            return new Event<T>(eventData)
            {
                Id = @event.Id,
                Sequence = @event.Sequence,
                TenantId = @event.TenantId,
                Version = @event.Version,
                StreamId = @event.StreamId,
                StreamKey = @event.StreamKey,
                Timestamp = @event.Timestamp
            };
        }
    }
}
