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

/*
 * jasperfx#679 (#5258). The seventh suite, and the first that needs NOTHING from the event store --
 * it is opt-in only in the sense that the fixture has to replay DocumentComplianceConfig.CommitListeners,
 * which MartenDocumentComplianceFixture now does.
 *
 * Different risk from the fifth and sixth suites rather than the same one. IDocumentCommitListener
 * and IDocumentChangeSet have no default implementations, so a near-miss on either is CS0535 rather
 * than a silent bind to a throwing default -- which is precisely why Marten bridges them with
 * DocumentCommitListenerAdapter + MartenDocumentChangeSet instead of widening IChangeSet, whose
 * IEnumerable<object> members would not satisfy the contract's IReadOnlyList<object> ones and whose
 * every existing implementor would break. What no compiler sees is the wiring, and that is what
 * these ten facts pin -- in particular the_change_set_survives_the_session_moving_on, which is the
 * one Marten can fail while doing everything else right: IChangeSet IS the session's live unit of
 * work and DocumentSessionBase.SaveChangesAsync resets it immediately after the listener loop.
 */
[Collection(DocumentComplianceCollection.Name)]
public class document_commit_listener_compliance
    : DocumentCommitListenerCompliance<MartenDocumentComplianceFixture>;

/*
 * jasperfx#819 (#5393). The eighth suite, and the first shared coverage of Guid optimistic
 * concurrency on ANY store -- the whole shared story of document concurrency was
 * NumericRevisionCompliance, and grepping the 2.68.0 suites for IVersioned returned nothing.
 *
 * marten#5372 is what lived in that gap on this side: a member mapped with Metadata.Version.MapTo(...)
 * was invisible to the session, so the upsert bound DBNull into its guard and reported every
 * cross-session write as a violation. fisher#245 is the same field one route over. Two stores, the
 * same failure mode, found independently and neither caught by anything shared.
 *
 * Gated on MartenDocumentComplianceFixture.SupportsOptimisticConcurrency, which also has to replay
 * DocumentComplianceConfig.OptimisticConcurrencyTypes -- see the comment on that loop in the fixture
 * for why dropping it fails every guard fact rather than skipping.
 *
 * In the collection like the rest even though it pins its own schema (compliance_concurrency) and so
 * could safely run beside them: the fixture's CleanDocumentDataAsync is a store-wide document wipe
 * called before every test, and keeping every document suite serialized locally is cheaper than
 * reasoning about which ones happen not to share a schema today.
 */
[Collection(DocumentComplianceCollection.Name)]
public class guid_optimistic_concurrency_compliance
    : GuidOptimisticConcurrencyCompliance<MartenDocumentComplianceFixture>;

/*
 * #5517 (jasperfx#898/#899). The ninth suite, 10 facts, and the first document coverage of conjoined
 * tenancy on any store. Opt-in through TWO fixture gates rather than one: SupportsConjoinedDocuments
 * for the storage style and SupportsCrossTenantQueries for the AnyTenant / TenantIsOneOf escapes,
 * which genuinely come apart -- a store can have conjoined storage and no cross-tenant escape.
 *
 * Three fixture obligations, and the first is the one that bites. Replaying
 * DocumentComplianceConfig.ConjoinedDocuments as a Conjoined TenancyStyle is NOT optional: drop it and
 * the isolation facts FAIL rather than skip, because a single-tenanted store folds both tenants'
 * writes into one row and each tenant reads the other's. Second, the two cross-tenant seam members --
 * Marten spells both escapes as element predicates inside the Where, recognized by the LINQ parser
 * from the extension method's declaring type, so they cannot be written in shared source at all.
 * Third, the suite opens tenant-scoped sessions through IDocumentSessionFactory, which needed the
 * explicit forwarders added to IDocumentStore in this change -- without them the suite calls the
 * contract's throwing defaults on tenancy that is entirely correct.
 */
[Collection(DocumentComplianceCollection.Name)]
public class document_conjoined_tenancy_compliance
    : DocumentConjoinedTenancyCompliance<MartenDocumentComplianceFixture>;

/*
 * #5543 (jasperfx#870/#927). The tenth suite, 32 facts, and the first cross-store coverage of the
 * surface a monitoring console browses documents through. Unlike the nine above it is not really
 * testing a capability Marten was missing -- IDocumentStoreDiagnostics has shipped since #545 -- it is
 * testing SEMANTICS that were never agreed. jasperfx#870 read Marten, Polecat and Fisher side by side
 * and found all three disagreeing on soft deletes, hierarchies, a missing tenant and how an id is
 * matched; Marten answered wrongly on every one, which is what #5543 fixes.
 *
 * Four fixture obligations. The two capability gates (SupportsDocumentDiagnostics /
 * SupportsDocumentDiagnosticWrites) hand over the reader and writer. The two config replays --
 * SoftDeletedDocuments and SubClasses -- are the same kind of load-bearing as ConjoinedDocuments above:
 * drop them and the facts fail rather than skip, because a hard-deleting type and a sub-class in its own
 * table are configurations where the behaviour under test cannot be observed at all.
 *
 * SupportsDocumentDiagnosticCriteria is deliberately left FALSE and is not a skip: with it false the
 * suite asserts Where / OrderBy are REFUSED with DocumentCriteriaNotSupportedException. That refusal is
 * the contract for a store with no predicate translation, because a console cannot tell an ignored
 * filter apart from one that matched every row. It flips when jasperfx#869's Dynamic LINQ lands.
 */
[Collection(DocumentComplianceCollection.Name)]
public class document_store_diagnostics_compliance
    : DocumentStoreDiagnosticsCompliance<MartenDocumentComplianceFixture>;

public static class DocumentComplianceCollection
{
    public const string Name = "document storage compliance";
}
