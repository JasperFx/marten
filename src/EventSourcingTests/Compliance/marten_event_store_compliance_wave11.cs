using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 11 -- the broadened cross-stream EventQuery surface, shipped in JasperFx 2.62.0
 * (jasperfx#737, merged as jasperfx#738). Same shape as the earlier enrollment files: an empty
 * subclass closing the shared suite over Marten's session pair.
 *
 * The suite covers every EventQuery filter field (single + multi event type names via
 * CombinedEventTypeNames, stream id, correlation/causation/user metadata, the inclusive
 * timestamp and sequence windows, and the folded EventTagQuerySpec tag conditions), the
 * sequence-ascending ordering contract, and the paging/TotalCount contract -- each fact
 * asserting exact membership so a silently-dropped filter fails rather than passes.
 *
 * Two fixture seams landed with the suite: the abstract SetUserName(TOperations, string?)
 * (Marten: session.LastModifiedBy) and ComplianceStoreConfig.EnableUserNameTracking (Marten:
 * Events.MetadataConfig.UserNameEnabled), both in MartenComplianceFixture.
 *
 * The EventQuery.TenantId filter is deliberately not exercised here -- it lives with
 * ConjoinedEventTenancyCompliance (wave 8 enrollment), which gained two EventQuery facts of
 * its own on the same bump.
 */

public class event_query_compliance
    : EventQueryCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
