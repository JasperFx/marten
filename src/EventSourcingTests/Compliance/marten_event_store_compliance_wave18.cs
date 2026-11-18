using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 18 -- the JasperFx 3.7 job seams (jasperfx#1215 / #1222).
 */

/// <summary>
///     jasperfx#1215 / #1222. The ceilings and cursors of stored-event rewrites, over <c>mt_event_range_marks</c>.
/// </summary>
public class event_range_mark_compliance
    : EventRangeMarkCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;

/// <summary>
///     jasperfx#1215. Marten's stored-event rewriter behind <c>IEventStore.RewriteStoredEventsAsync</c>, with a
///     transform that is not idempotent, so a page applied twice shows up.
/// </summary>
public class event_rewrite_compliance
    : EventRewriteCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
