using System;
using JasperFx;
using JasperFx.Events.Tags;

namespace Marten.Events.Dcb;

/// <summary>
/// Thrown when a DCB consistency check fails — new events matching the tag query
/// were appended after the boundary was established.
/// </summary>
public class DcbConcurrencyException: ConcurrencyException
{
    public DcbConcurrencyException(EventTagQuery query, long lastSeenSequence)
        : base($"DCB consistency violation: new events matching the tag query were appended after sequence {lastSeenSequence}")
    {
        Query = query;
        LastSeenSequence = lastSeenSequence;
    }

    public EventTagQuery Query { get; }
    public long LastSeenSequence { get; }
}
