using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 14 (#5343) -- the JasperFx 2.64.0 compliance wave, and the largest single enrollment so far.
 *
 * What makes this wave different from every one before it: NONE of these suites had ever been
 * executed against a real event store. The JasperFx repo enrolls the document suites but no event
 * store, so all of the below shipped compile-checked and design-reasoned only, and Marten running
 * them here is their first runtime validation. That is the point of the exercise rather than a
 * caveat on it -- see the PR for what the first run actually found.
 *
 * Same shape as the earlier enrollment files: empty subclasses closing the shared suites over
 * Marten's session pair. What is NOT empty is MartenComplianceFixture, which grows a seam member
 * for most of these; each suite gates on a Supports... flag defaulting false plus a seam with a
 * throwing default, so enrolling means implementing the Marten half. The flags Marten leaves false
 * are documented at their declaration in the fixture rather than here.
 */

/// <summary>
///     jasperfx#764/#765 (+#772). Marten's [NaturalKey] storage half -- the mt_natural_key_X lookup
///     table maintained by the auto-registered NaturalKeyProjection (#4788) -- resolving the shared
///     FetchForWriting / FetchForExclusiveWriting / FetchLatest triple through it, including the
///     uniqueness ruling that a second claimant of a live key is refused.
/// </summary>
public class natural_key_compliance
    : NaturalKeyCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#754. The AggregateToAsync half: folding an ad hoc raw-event query into a single
///     aggregate over Marten's derived live aggregator, with identity stamped from the stream.
/// </summary>
public class aggregate_to_linq_operator_compliance
    : AggregateToLinqOperatorCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#754. The AggregateToManyAsync half, and the more demanding of the pair: the operator
///     has to drive the REGISTERED multi-stream projection -- identity routing, a session-reading
///     custom grouper, ShouldDelete -- rather than reimplementing the fold inline. The projections
///     are registered Async with the daemon never started, so every aggregate the suite asserts on
///     can only have come from the live fold.
/// </summary>
public class aggregate_to_many_compliance
    : AggregateToManyCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#755. HasTag&lt;TTag&gt; predicates inside an event LINQ query, composed with ordinary
///     event predicates in ONE predicate tree -- which is why the fixture seam applies its filter in
///     a single Where() rather than chaining.
/// </summary>
public class dcb_has_tag_linq_compliance
    : DcbHasTagLinqCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#763. RaiseSideEffects: raised events, published messages, and their suppression
///     during a rebuild. The expensive one to enroll (a projection base alias, a registrar member and
///     two consumer partials on RecordingMessageOutbox/RecordingMessageBatch), and worth it -- the
///     half underneath the shared raise seam is each store's own, and has shipped STUBBED EMPTY
///     twice (fisher#61, polecat#420), dropping every raised event with no error and no log. A store
///     that did that passes every other suite in the library.
/// </summary>
public class projection_side_effect_compliance
    : ProjectionSideEffectCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#762. AlwaysEnforceConsistency -- the store-wide opt-in that makes every append
///     assert the stream version it was handed.
/// </summary>
public class always_enforce_consistency_compliance
    : AlwaysEnforceConsistencyCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#762. Marten's FetchStreamStatePlan / FetchStreamPlan, run BOTH standalone and inside
///     a batched query -- the two paths compose their SQL separately, so a plan that is right one way
///     and wrong the other is exactly what this suite is looking for.
/// </summary>
public class stream_query_plan_compliance
    : StreamQueryPlanCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#769. The shared ProjectionScenario harness (lifted into JasperFx.Events.TestSupport
///     in 2.38.0) reached through Marten's own documented entry point,
///     Advanced.EventProjectionScenario -- the route is under test as much as the harness, so the
///     fixture seam forwards to it rather than reimplementing its three lines.
/// </summary>
public class projection_scenario_compliance
    : ProjectionScenarioCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#732/#760. The one suite here with NO capability gate and no skip, deliberately: it
///     asserts that Marten's DOCUMENTED DI registration -- AddMarten(...).AddAsyncDaemon(...) --
///     produces a reachable IProjectionCoordinator. Every other daemon suite drives a daemon the
///     fixture built by hand, which can never observe that; fisher#138 shipped exactly that gap and
///     passed all 37 suites while it did. A store that enrolls this without implementing
///     StartCoordinatorHostAsync fails every fact rather than skipping, because a skippable
///     registration check recreates the silent gap the suite exists to close.
/// </summary>
public class projection_coordinator_compliance
    : ProjectionCoordinatorCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#752/#757. Enrolled but SKIPPING wholesale, and deliberately so rather than as
///     unfinished work here: this is the first suite in the library written ahead of any store
///     implementing the behavior. Marten has had upcasting since v5, but it is Marten's own
///     implementation rather than the shared JasperFx.Events.Upcasting registry the suite asserts on
///     -- see the SupportsUpcasting comment on MartenComplianceFixture for the detail. Enrolling it
///     now means it compiles and runs from the moment the flag flips, instead of being remembered.
/// </summary>
public class upcasting_compliance
    : UpcastingCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
