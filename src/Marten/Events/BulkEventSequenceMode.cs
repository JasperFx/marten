namespace Marten.Events;

/// <summary>
/// Governs how the streaming bulk event import (<see cref="Marten.IDocumentStore.BulkInsertEventStreamAsync"/>)
/// assigns <c>seq_id</c> values to the imported events.
/// </summary>
public enum BulkEventSequenceMode
{
    /// <summary>
    /// Default. Each imported event is assigned the next fresh value from the store's event sequence —
    /// the tenant's own <c>mt_events_sequence_{suffix}</c> under per-tenant event partitioning, otherwise
    /// the store-global <c>mt_events_sequence</c>. Cross-stream arrival order is preserved in the
    /// assigned values, but the numbers themselves are new. Use for seeding fresh data or importing
    /// from a source whose sequence numbers carry no meaning in the target.
    /// </summary>
    AssignFromSequence,

    /// <summary>
    /// Migration mode. Each imported event keeps the <c>Sequence</c> it already carries — historical
    /// events are never renumbered, so progression rows, downstream warehouses, audit logs, and any
    /// external consumer that captured a sequence position stay valid. The supplied events must arrive
    /// in strictly ascending <c>Sequence</c> order (per-tenant gaps are fine and expected — a conjoined
    /// source interleaves tenants on one global sequence). After the copy the target sequence is advanced
    /// past the imported maximum via <c>setval</c> so the first live append can never re-issue an
    /// imported <c>seq_id</c>, and under per-tenant event partitioning the tenant's
    /// <c>HighWaterMark:{tenantId}</c> progression row is seeded at that maximum so high-water detection
    /// starts from a persisted mark above the (gappy) imported history.
    /// </summary>
    PreserveSourceSequence
}
