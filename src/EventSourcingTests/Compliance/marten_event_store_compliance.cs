using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Marten's enrollment in the cross-store event sourcing compliance suites. Each class below is
 * empty on purpose: the behavior lives once in JasperFx.Events.ComplianceTests and is closed here
 * over Marten's IEventStore<IDocumentOperations, IQuerySession> session pair through
 * MartenComplianceFixture. Polecat -- and later the Sqlite minimal store -- enroll the same way.
 */

public class self_aggregating_evolve_compliance
    : SelfAggregatingEvolveCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class dcb_tag_query_and_consistency_compliance
    : DcbTagQueryAndConsistencyCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class assign_tag_where_compliance
    : AssignTagWhereCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class async_daemon_compliance
    : AsyncDaemonCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class auto_discovered_aggregate_compliance
    : AutoDiscoveredAggregateCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class event_projection_registration_compliance
    : EventProjectionRegistrationCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class event_projection_enrichment_compliance
    : EventProjectionEnrichmentCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class rebuild_concurrency_cap_compliance
    : RebuildConcurrencyCapCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
