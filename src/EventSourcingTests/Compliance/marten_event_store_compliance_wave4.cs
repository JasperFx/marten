using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 4 enrollments. Same shape as marten_event_store_compliance.cs -- empty subclasses closing the
 * shared suites over Marten's session pair. Kept in a separate file only while the suites are still
 * in flight upstream; fold into the main enrollment file once the JasperFx package ships them.
 */

public class fetch_for_writing_compliance
    : FetchForWritingCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class stream_read_compliance
    : StreamReadCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class event_metadata_compliance
    : EventMetadataCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class live_aggregation_compliance
    : LiveAggregationCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
