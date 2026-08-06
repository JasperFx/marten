using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 5 enrollments. Same shape as marten_event_store_compliance.cs -- empty subclasses closing
 * the shared suites over Marten's session pair. Separate file only while the suites are in flight
 * upstream; fold into the main enrollment file once the JasperFx package ships them.
 */

public class fetch_latest_compliance
    : FetchLatestCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class stream_archiving_compliance
    : StreamArchivingCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class event_store_explorer_compliance
    : EventStoreExplorerCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class flat_table_projection_compliance
    : FlatTableProjectionCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class string_stream_identity_compliance
    : StringStreamIdentityCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class multi_stream_projection_compliance
    : MultiStreamProjectionCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

public class snapshot_lifecycle_compliance
    : SnapshotLifecycleCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
