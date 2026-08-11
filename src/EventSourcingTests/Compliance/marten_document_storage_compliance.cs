using JasperFx.Events.ComplianceTests;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Compliance;

/*
 * Document storage compliance (#5216, jasperfx#647). Same shape as the event store enrollments --
 * empty subclasses closing the shared suites over Marten's fixture -- but notably NOT generic over
 * Marten's session pair, because the document contract is reachable entirely through
 * IDocumentSessionFactory and the three session interfaces.
 *
 * These live alongside the event enrollments rather than in DocumentDbTests because this is the
 * project that compiles the compliance sources (and carries the -p:ComplianceSourceDir dev-loop
 * switch for validating in-flight upstream suites before a JasperFx release).
 *
 * All four share one xUnit collection, and that is load-bearing rather than tidiness. Every one of
 * these suites pins its DocumentComplianceConfig.SchemaName to "compliance_documents", so they all
 * resolve to the same physical schema -- and DocumentStorageComplianceSuite calls
 * CleanDocumentDataAsync in InitializeAsync, before every test. Run the classes in parallel and one
 * class's per-test wipe lands in the middle of another's test. A collection serializes them.
 * (CI sets DISABLE_TEST_PARALLELIZATION, so this only bites locally -- which is exactly where a
 * green run matters most while working.)
 */

[Collection(DocumentComplianceCollection.Name)]
public class document_load_and_store_compliance
    : DocumentLoadAndStoreCompliance<MartenDocumentComplianceFixture>;

[Collection(DocumentComplianceCollection.Name)]
public class document_query_compliance
    : DocumentQueryCompliance<MartenDocumentComplianceFixture>;

[Collection(DocumentComplianceCollection.Name)]
public class document_delete_compliance
    : DocumentDeleteCompliance<MartenDocumentComplianceFixture>;

[Collection(DocumentComplianceCollection.Name)]
public class document_session_compliance
    : DocumentSessionCompliance<MartenDocumentComplianceFixture>;

public static class DocumentComplianceCollection
{
    public const string Name = "document storage compliance";
}
