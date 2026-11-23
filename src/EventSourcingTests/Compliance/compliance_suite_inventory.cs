using System;
using System.Collections.Generic;
using JasperFx.Events.ComplianceTests;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Compliance;

/// <summary>
///     jasperfx#1091 / marten#5827. A compliance suite can never be left out of this assembly silently:
///     every leaf suite has a concrete subclass closed over Marten's fixture, or an excuse with a reason.
/// </summary>
public class compliance_suite_inventory
{
    [Fact]
    public void every_event_store_compliance_suite_is_enrolled_or_excused()
    {
        ComplianceSuiteInventory.AssertEnrolled(typeof(compliance_suite_inventory).Assembly,
            new MartenComplianceFixture(), new Dictionary<Type, string>
            {
                [typeof(BackfillCompliance<,,>)] =
                    "Marten does not run projection backfills yet (marten#5632): a version-bumped single-stream "
                    + "projection writes a new table, so Marten has to keep the old one for a projection that "
                    + "declares a backfill first. SupportsBackfills stays false until then"
            });
    }

    [Fact]
    public void every_document_compliance_suite_is_enrolled_or_excused()
    {
        ComplianceSuiteInventory.AssertEnrolled(typeof(compliance_suite_inventory).Assembly,
            new MartenDocumentComplianceFixture(), new Dictionary<Type, string>
            {
                [typeof(DocumentSearchCompliance<>)] =
                    "Enrolled in Marten.PgVector.Tests (pgvector_document_search_compliance), which has the "
                    + "pgvector image the suite needs; this assembly runs against plain PostgreSQL"
            });
    }
}
