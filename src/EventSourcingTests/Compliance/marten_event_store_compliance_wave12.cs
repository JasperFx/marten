using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 12 -- the streams table as a real IQueryable<StreamState>, shipped in JasperFx 2.63.0
 * (jasperfx#740, merged as jasperfx#741). Same shape as the earlier enrollment files: an empty
 * subclass closing the shared suite over Marten's session pair.
 *
 * The suite covers one Where() fact per public get member of StreamState -- including the new
 * CompactedVersion watermark and the AggregateType == typeof(X) form, which is the Stream
 * Compaction Policy's selector -- plus the compaction-policy predicate itself
 * (AggregateType == typeof(X) && Version - CompactedVersion > N && !IsArchived), the stated
 * OrderBy(Created).ThenBy(Id) ordering with Skip/Take paging, the shared async terminators, and
 * truthful empty answers.
 *
 * Marten's implementation is the dedicated provider in Marten.Events.Querying (see
 * StreamStateQueryable.cs for why it is not the general LINQ engine), and the watermark is
 * written by CompactStreamAsync via RecordCompactionWatermarkOperation.
 *
 * The tenant-scoped overload (QueryStreamStates(tenantId)) is deliberately not exercised here --
 * it lives with ConjoinedEventTenancyCompliance (wave 8 enrollment), which gained a fact for it
 * on the same bump. The refusal shapes the shared suite deliberately cannot pin (an
 * untranslatable member, a tenant scope on a tenantless store) are pinned in Marten's own
 * EventSourcingTests/query_stream_states_refusals.cs.
 */

public class stream_state_query_compliance
    : StreamStateQueryCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
