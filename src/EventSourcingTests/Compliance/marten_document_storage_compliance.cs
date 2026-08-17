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

/*
 * jasperfx#669. The fifth suite, and the only opt-in one of the five -- it requires the store to be
 * an event store as well as a document store. It exists to catch a silent failure: C# interface
 * implementation is not return-type covariant, so Marten's sessions, which already declared an
 * `Events` property of Marten's OWN IQueryEventStore / IEventStoreOperations, did not implement
 * IDocumentReadOperations.Events or IDocumentSessionOperations.Events at all -- both bound to the
 * interfaces' throwing default implementations with no compile error anywhere. Both tiers now carry
 * an explicit interface implementation (QuerySession / DocumentSessionBase) and this is what pins
 * them.
 */
[Collection(DocumentComplianceCollection.Name)]
public class document_session_events_compliance
    : DocumentSessionEventsCompliance<MartenDocumentComplianceFixture>;

/*
 * jasperfx#673 (#5250). The sixth suite, opt-in for the same reason as the fifth, and reusing its
 * event types -- so the two are enrolled together. Same trap again, one layer down: Marten's
 * PendingChanges.Streams() returns IList<StreamAction>, and IList<T> is not assignable to
 * IReadOnlyList<T>, so it did not satisfy IDocumentSessionOperations.PendingStreams either.
 * DocumentSessionBase now carries the explicit implementation and this is what pins it. The suite's
 * first fact -- an empty collection on a session with nothing enlisted -- is precisely the one a
 * store still on the interface's throwing default cannot pass.
 */
[Collection(DocumentComplianceCollection.Name)]
public class pending_stream_actions_compliance
    : PendingStreamActionsCompliance<MartenDocumentComplianceFixture>;

public static class DocumentComplianceCollection
{
    public const string Name = "document storage compliance";
}
