using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 7 enrollments. Separate file only while the suites are in flight upstream; fold into the
 * main enrollment file once the JasperFx package ships them.
 */

public class rebuild_and_catch_up_compliance
    : RebuildAndCatchUpCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class dead_letter_compliance
    : DeadLetterCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
