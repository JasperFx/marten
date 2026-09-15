using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * #5383 part 5 — the multi-database arms of the explorer compliance.
 *
 * EventStoreExplorerCompliance covers the database-scoped overloads on a SINGLE-database store, where
 * they can only be asserted to AGREE with the store-global read — which is vacuously true of a store
 * that ignores the argument entirely. These two arms are where the argument has to mean something.
 *
 * They split on independent axes and neither can see the other's gap:
 *
 *   DatabasePerTenantExplorerCompliance — distinct databases, deliberately NOT conjoined, so there is
 *   no tenant_id column and the arm is blind to a dropped predicate. What it pins is that a
 *   store-global read on a multi-database store is not a silent partial answer. Marten fans out and
 *   merges for the listings and REFUSES for the two single-stream reads (#5383 part 3); the suite
 *   accepts either remedy, because what it rules out is one database's worth returned as though it
 *   were everything — CritterWatch#1231, a console over 512 shard databases.
 *
 *   ShardedTenancyExplorerCompliance — two tenants CO-LOCATED in one shard with conjoined events, which
 *   is the only configuration that needs both axes at once: open the tenant's database AND filter by
 *   tenant_id. This is gap 2, and Marten failed it until this branch — reproduced first in
 *   sharded_explorer_tenant_scoping_5383, where a tenant-scoped read returned a co-located tenant's
 *   streams and a stream read interleaved two tenants' events into one version-ordered sequence.
 */

public class marten_database_per_tenant_explorer_compliance
    : DatabasePerTenantExplorerCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class marten_sharded_tenancy_explorer_compliance
    : ShardedTenancyExplorerCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
