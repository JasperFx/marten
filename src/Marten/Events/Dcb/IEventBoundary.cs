using System.Collections.Generic;
using JasperFx.Events;

namespace Marten.Events.Dcb;

/// <summary>
/// Represents the result of a Dynamic Consistency Boundary (DCB) query with
/// consistency enforcement. Events are loaded by tag query, aggregated into T,
/// and Marten will assert no new matching events were added at SaveChangesAsync() time.
/// </summary>
/// <typeparam name="T">The aggregate type projected from the matching events</typeparam>
public interface IEventBoundary<out T> where T : notnull
{
    /// <summary>
    /// The aggregate projected from the events matching the tag query.
    /// May be null if no matching events were found.
    /// </summary>
    T? Aggregate { get; }

    /// <summary>
    /// The maximum seq_id from the tag query results.
    /// Used as the consistency boundary marker.
    /// </summary>
    long LastSeenSequence { get; }

    /// <summary>
    /// The events that matched the tag query, ordered by seq_id.
    /// </summary>
    IReadOnlyList<IEvent> Events { get; }

    /// <summary>
    /// Append an event. The event MUST have tags set via WithTag()
    /// so Marten can route it to the appropriate stream(s).
    /// </summary>
    void AppendOne(object @event);

    /// <summary>
    /// Append multiple events. Each event MUST have tags set via WithTag().
    /// </summary>
    void AppendMany(params object[] events);

    /// <summary>
    /// Append multiple events. Each event MUST have tags set via WithTag().
    /// </summary>
    void AppendMany(IEnumerable<object> events);
}
