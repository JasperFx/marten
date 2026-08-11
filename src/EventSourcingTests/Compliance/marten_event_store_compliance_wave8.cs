using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 8 enrollments -- the last two suites in the event sourcing compliance backlog, shipped in
 * JasperFx 2.45.0 (jasperfx#642). Same shape as the earlier enrollment files: empty subclasses
 * closing the shared suites over Marten's session pair.
 *
 * ConjoinedEventTenancyCompliance (#5148) needed no tenant-scoped session seam in the end --
 * OpenSession(IEventDatabase, tenantId) is already on the shared generic IEventStore<,> and
 * TenancyStyle is already a shared JasperFx.MultiTenancy enum, so the only seam addition was the
 * ComplianceStoreConfig.ConjoinedEventTenancy switch MartenComplianceFixture reads.
 *
 * SubscriptionCompliance (#5151) is the one suite that cannot be closed by an empty subclass alone:
 * ISubscription's ProcessEventsAsync returns a per-product IChangeListener, so the shared
 * ComplianceSubscription is a partial class completed by ComplianceSubscription.Marten.cs beside
 * MartenComplianceFixture.
 */

public class conjoined_event_tenancy_compliance
    : ConjoinedEventTenancyCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class subscription_compliance
    : SubscriptionCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
