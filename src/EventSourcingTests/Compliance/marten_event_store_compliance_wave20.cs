using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 20 -- aggregate types and stream stats by aggregate type (JasperFx 3.13, jasperfx#1282, marten#6152).
 */

/// <summary>
///     marten#6152. <c>IEventStore.RegisteredAggregateTypes()</c> answers from the projection graph, and
///     <c>MartenDatabase.FetchAggregateStreamStatsAsync</c> counts <c>mt_streams</c> by aggregate type in one round trip.
/// </summary>
public class aggregate_stream_stats_compliance
    : AggregateStreamStatsCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
