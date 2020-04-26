using System;
using System.Collections.Generic;

namespace Marten.V4
{
    public class MyMetadata
    {
        public Guid CorrelationId { get; set; }
        public string UserId { get; set; }
    }

    public enum StreamState
    {
        CreateNew,
        Append,
        AppendOrCreate,
        Pending,
        History
    }

    // This really represents a segment of the underlying stream
    public abstract class EventStreamBase<TKey, TEvent, TEventBase> where TEvent : IEvent<TEventBase>
    {
        public TKey Id { get; set; }

        // There's a little bit of optimization for inline
        // projections by doing this

        // Other folks want different mechanics
        public StreamState State { get; set; }

        public IList<TEvent> Events { get; } = new List<TEvent>();


        // Data in here while it's being appended, but moves up above when
        // it's pushed into the database
        public IList<TEventBase> Pending { get; } = new List<TEventBase>();
    }

    public interface IEvent<TEventBase>
    {
        Guid Id { get; set; }
        int Version { get; set; }
        long Sequence { get; set; }
        TEventBase Data { get; set; }
        DateTimeOffset Timestamp { get; set; }

        // Additional, user-defined metadata properties
        // like user id or correlation id or whatever

    }

    // This might be too heavyweight
    public interface IMultitenantEvent<TEventBase>: IEvent<TEventBase>
    {
        string TenantId { get; set; }
    }


}
