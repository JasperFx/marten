using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 13 (#5335) -- the last two shipped suites Marten did not enroll, both arriving in the
 * JasperFx 2.59.0 line. Same shape as the earlier enrollment files: empty subclasses closing the
 * shared suites over Marten's session pair.
 *
 * CompositeProjectionCompliance (jasperfx#725) is opt-in through the registrar's
 * AddCompositeProjection seam member, implemented in MartenComplianceFixture on this wave as the
 * documented forward-plus-adapter over Projections.CompositeProjectionFor. The suite pins the
 * shared composite contract: every stage materializes from one async pass, a rebuild tears the
 * members down rather than replaying over their surviving rows (the Bug 5175 class of failure),
 * and the composite presents itself as exactly one shard. Marten's own coverage of the same
 * ground lives in src/DaemonTests/Composites/.
 *
 * SingleTenantedEventSlicingCompliance (jasperfx#724) pins marten#4085: events whose tenant_id
 * values disagree on a single-tenanted store still fold into ONE async aggregate rather than
 * being sliced per tenant into partial documents. Per jasperfx#727 the suite's precondition is
 * only constructible on Marten -- Polecat and Fisher normalize the stamped tenant ids away and
 * hit the suite's honest-skip guard -- which is exactly why Marten, where the bug was reported
 * and fixed, is the store that should be holding the contract while that issue is settled.
 */

public class composite_projection_compliance
    : CompositeProjectionCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class single_tenanted_event_slicing_compliance
    : SingleTenantedEventSlicingCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
