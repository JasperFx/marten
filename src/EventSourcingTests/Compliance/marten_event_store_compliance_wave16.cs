using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 16 -- the JasperFx 2.102.0 compliance wave.
 */

/// <summary>
///     jasperfx#1150. MartenDatabase.FetchFirstEventTimestampsAfterAsync: the timestamp of the first
///     non-archived event past each position, one answer per position in the order asked, narrowed to a
///     tenant under conjoined tenancy. Per-tenant partitioned events are covered in
///     EventSourcingTests.Daemon.pending_event_ages_with_tenant_partitioned_events.
/// </summary>
public class pending_event_age_compliance
    : PendingEventAgeCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
