using JasperFx.Events.ComplianceTests;
using Marten;
using Marten.Testing.Harness;

namespace EventSourcingTests.Compliance;

/*
 * Wave 10 -- the second-level FetchForWriting aggregate snapshot cache, shipped in JasperFx 2.51.0
 * (jasperfx#674). Marten is the reference implementation again: the shape originated here as the
 * feature/aggregate-cache-fetchforwriting spike and was promoted into JasperFx.Events so Marten,
 * Polecat and Fisher share one contract rather than each growing its own.
 *
 * Opt-in, like wave 9: a store that has not wired IAggregateWriteCache into its fetch plans does not
 * enroll, and IComplianceStoreRegistrar.CacheAggregatesForWriting keeps its throwing default.
 *
 * The subject of the suite is that turning caching ON is unobservable except in latency -- a hit is
 * indistinguishable from a miss, including when the baseline is stale, ahead of the stream, or
 * evicted -- plus the OCC fact that a warm cache does not suppress a concurrency failure. Every one
 * of those is vacuously true of a store that quietly ignored the opt-in, so the suite supplies its
 * own RecordingAggregateWriteCache and asserts a nonzero hit count, exactly the way the binary
 * serialization suite gzips to prove its serializer ran.
 *
 * It covers both lifecycles with the daemon deliberately never started, so the Async aggregate's
 * stored snapshot always lags and every fetch has a real delta to fold. It does NOT pin when an
 * entry is written, because that genuinely differs by lifecycle -- Marten writes the Async entry as
 * soon as the fetch completes, and defers the Inline one to AfterCommitAsync because the inline
 * projection mutates the very instance the caller was handed.
 *
 * The Marten-specific tests in EventSourcingTests/FetchForWriting/caching_*_aggregates_for_writing
 * stay: they assert things the shared suite deliberately does not, notably that a hit folds ONLY
 * the delta (counted in queries rather than watched on a hit counter) and that a failed commit
 * leaves no entry behind, inspected directly on the cache.
 */

public class aggregate_write_cache_compliance
    : AggregateWriteCacheCompliance<MartenComplianceFixture, IDocumentOperations, IQuerySession>;
