namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// One stream's complete event sequence for a single tenant. Each batch is
/// the complete contents of a single <c>StartStream&lt;TAggregate&gt;</c> +
/// <c>SaveChangesAsync</c> call — no inter-batch coordination is required,
/// so the writer pool can fan out fully in parallel without colliding on the
/// per-stream version sequence.
///
/// <para>
/// Cross-stream interleaving at the <c>mt_events</c> table level still
/// happens via the producer's draw order: it picks the next stream to emit
/// using a weighted random across stream types per tenant, then round-robins
/// across tenants. Writers commit roughly in producer order, so the events
/// table ends up with the same interleaving shape a real workload exhibits.
/// </para>
/// </summary>
/// <param name="TenantId">Conjoined tenant id; never null.</param>
/// <param name="StreamId">Stream id; stable so a given stream id always maps to one StartStream call.</param>
/// <param name="AggregateType">Aggregate the stream rolls up to (Appointment / Board / ProviderShift).</param>
/// <param name="Events">The complete event sequence for the stream, in append order.</param>
internal sealed record EventBatch(
    string TenantId,
    Guid StreamId,
    Type AggregateType,
    IReadOnlyList<object> Events);
