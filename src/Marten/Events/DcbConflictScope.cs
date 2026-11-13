namespace Marten.Events;

/// <summary>
/// What makes a later append conflict with a Dynamic Consistency Boundary read by
/// <c>FetchForWritingByTags</c>. See #5887.
/// </summary>
/// <remarks>
/// The DCB append condition fails when an event matching the boundary's query was appended after
/// the read, and a query's condition can name event types as well as a tag value
/// (<c>EventTagQuery.AndEventsOfType</c>). Marten detects conflicts through a version it keeps per
/// tag value in <c>mt_dcb_tag_version</c>, and this setting decides whether it also keeps one per
/// (tag value, event type) and honours the event types when it checks.
/// <para>
/// <b>Changing this on a running system needs care.</b> <see cref="TagValueAndEventType"/> checks
/// versions that only nodes running <see cref="TagValueRecordingEventTypes"/> or
/// <see cref="TagValueAndEventType"/> maintain. While any node still appends tagged events under
/// <see cref="TagValue"/>, a node checking per event type can miss that node's conflicting appends.
/// Roll out <see cref="TagValueRecordingEventTypes"/> to every node first, then switch to
/// <see cref="TagValueAndEventType"/>. That includes upgrading from Marten 9, whose default is
/// <see cref="TagValue"/>.
/// </para>
/// </remarks>
public enum DcbConflictScope
{
    /// <summary>
    /// The default in Marten 9. Any appended event carrying a tag value the boundary read conflicts
    /// with it, whatever its event type. Conservative: it never misses a conflict, and reports some
    /// that the DCB specification would not -- an event of a type the query does not select.
    /// </summary>
    TagValue,

    /// <summary>
    /// The rolling-upgrade step between <see cref="TagValue"/> and <see cref="TagValueAndEventType"/>.
    /// Conflicts are detected exactly as for <see cref="TagValue"/>, but every append also maintains
    /// the per-event-type versions that <see cref="TagValueAndEventType"/> checks. Costs one extra
    /// <c>mt_dcb_tag_version</c> row write per (tag, event type) pair appended.
    /// </summary>
    TagValueRecordingEventTypes,

    /// <summary>
    /// The default since Marten 10, and the DCB specification's append condition. A query condition that names event types only
    /// conflicts with appended events of those types; a condition without event types conflicts with
    /// any event carrying its tag value. Maintains the same extra versions as
    /// <see cref="TagValueRecordingEventTypes"/>. Every node appending tagged events must be running
    /// <see cref="TagValueRecordingEventTypes"/> or this before any node switches to it.
    /// </summary>
    TagValueAndEventType
}
