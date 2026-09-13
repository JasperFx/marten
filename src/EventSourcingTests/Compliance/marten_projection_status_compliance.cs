using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * jasperfx#818 (#5392). GetProjectionStatusesAsync is the snapshot a monitoring console's projections
 * page renders before it subscribes to ShardStatesChanged. Marten, Polecat and Fisher all implement
 * it, nothing shared held any of them to it, and the three stores had each decided independently --
 * and reasonably -- what the same five ShardStatus fields meant.
 *
 * Eight of the nine facts passed here as they stood, including the two that cost Polecat work:
 * EventStoreSequence is the head of the event store rather than the high-water row (sourcing it from
 * that row makes every shard on a stopped daemon look caught up, which is the opposite of what a
 * projections page is opened to find out), and the State slot never carries a lifecycle.
 *
 * The ninth is a_reachable_running_daemon_reports_the_real_shard_state, and it is the one that needed
 * a product change: Marten answered Unknown unconditionally, never reading a daemon at all, so the
 * field carried no information. State is now read from whichever daemons are running against this
 * store -- see DocumentStore.EventStoreExplorer.readAgentStatesAsync for why it goes through
 * AllDaemonsAsync() rather than DaemonForDatabase, and why daemons are matched by StoreUri.
 *
 * Note the suite does NOT require an empty shard list for an Inline projection. jasperfx#818 proposed
 * it; Marten reports SignalBoard:All / Unknown / 0 / 0, which is a coherent answer of a different
 * kind -- a shard name is a registry fact, Projections.All has one for an inline registration, and
 * "here is the shard, nothing is running it" is the same reading of Unknown used everywhere else.
 * Marten's behaviour stands and the requirement was dropped.
 */
public class marten_projection_status_compliance
    : ProjectionStatusCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
