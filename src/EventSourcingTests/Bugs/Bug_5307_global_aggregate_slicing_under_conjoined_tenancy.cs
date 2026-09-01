using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

// #5307, split out of #5305. Two places decide whether a stream should be treated as
// single-tenanted, and they disagree:
//
//   * FetchAsyncPlan counts a GLOBAL AGGREGATE as single-tenanted
//     (`TenancyStyle == Single || GlobalAggregates.Contains(typeof(TDoc))`).
//   * SingleStreamProjection.BuildSlicer looks only at `TenancyStyle == Single`.
//
// So on a conjoined store a global aggregate is FETCHED as global but its events are SLICED per
// tenant. AddGlobalProjection deliberately creates that mismatch -- the document is made
// single-tenanted while the events stay conjoined -- and the projection's own validation permits it
// via the !IsGlobalWithinConjoinedTenancy guard. Until JasperFx 2.58.0 the difference could not have
// mattered, because TenantedEventSlicer honoured ForceSingleTenancy on only one of its two overloads
// and the async daemon reaches the other (jasperfx#721). #5306 bumped to 2.59.0, which makes the flag
// live and the asymmetry observable for the first time.
//
// ANSWER: the asymmetry is harmless, because the hazard it would cause is prevented one layer
// earlier. GlobalEventAppenderDecorator forces every stream belonging to a global aggregate onto the
// default tenant AT WRITE TIME, so a global aggregate's stream cannot carry disagreeing tenant_ids in
// the first place and per-tenant slicing has nothing to split. These tests pin that, rather than the
// slicer's behaviour, because that is where the guarantee actually lives -- and they probe both of the
// appender's two matching rules, since a gap in either one would reopen the question.
public class Bug_5307_global_aggregate_slicing_under_conjoined_tenancy: OneOffConfigurationsContext
{
    public record TallyIncremented(int Amount);

    // Deliberately NOT applied by GlobalTally. GlobalEventAppenderDecorator.Matches has two rules --
    // the stream's AggregateType is a global aggregate, or one of the appended events is an event type
    // the global projection includes. An event satisfying neither is the one shape that could still
    // reach storage under a non-default tenant, so it needs its own case.
    public record UnrelatedNoise(string What);

    // Additive on purpose, exactly as in Bug_4085: a stream folded once and a stream folded in pieces
    // differ in these numbers, where a last-write-wins aggregate would hide the split.
    public class GlobalTally
    {
        public Guid Id { get; set; }
        public int Total { get; set; }
        public int EventCount { get; set; }

        public void Apply(TallyIncremented e)
        {
            Total += e.Amount;
            EventCount++;
        }
    }

    private void ConfigureGlobalProjectionStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AddEventType<UnrelatedNoise>();

            // What creates the mismatch under test: TDoc joins EventGraph.GlobalAggregates,
            // IsGlobalWithinConjoinedTenancy is set, and the DOCUMENT is overridden to single-tenanted
            // while the EVENTS stay conjoined.
            opts.Projections.AddGlobalProjection(new SingleStreamProjection<GlobalTally, Guid>(),
                ProjectionLifecycle.Async);
        });
    }

    /// <summary>
    /// The mechanism, pinned directly. A cross-tenant append to a global aggregate's stream is accepted
    /// -- and lands on the default tenant anyway.
    /// </summary>
    [Fact]
    public async Task a_global_aggregates_stream_is_normalised_to_the_default_tenant_on_write()
    {
        ConfigureGlobalProjectionStore();

        var streamId = Guid.NewGuid();

        // Assert the session really does carry a non-default tenant, so that the DEFAULT below is
        // Marten normalising rather than the test never having configured tenancy at all. Not
        // establishing this is how the original probe on #5305 produced a result that proved nothing.
        await using (var blue = theStore.LightweightSession("blue"))
        {
            blue.TenantId.ShouldBe("blue");

            blue.Events.StartStream<GlobalTally>(streamId, new TallyIncremented(1));
            await blue.SaveChangesAsync();
        }

        // A cross-tenant append to an already-existing stream. EventGraph.Append stamps the
        // StreamAction with the SESSION's tenant, so absent the global-appender decorator this is all
        // it would take to get disagreeing tenant_ids onto one stream.
        await using (var red = theStore.LightweightSession("red"))
        {
            red.Events.Append(streamId, new TallyIncremented(2), new TallyIncremented(4));
            await red.SaveChangesAsync();
        }

        // Matched by BOTH of the decorator's rules here: the first append by AggregateType, the second
        // by TallyIncremented being an event type the global projection includes.
        var tenantIds = await tenantIdsOnStreamAsync(streamId);
        tenantIds.ShouldBe([StorageConstants.DefaultTenantId]);
    }

    /// <summary>
    /// And the consequence: with the whole stream on one tenant, per-tenant slicing has nothing to
    /// split and the daemon folds it once. This is the assertion #5305 wanted and could not make.
    /// </summary>
    [Fact]
    public async Task the_async_daemon_folds_a_global_aggregate_once_under_conjoined_tenancy()
    {
        ConfigureGlobalProjectionStore();

        var streamId = Guid.NewGuid();

        await using (var blue = theStore.LightweightSession("blue"))
        {
            blue.Events.StartStream<GlobalTally>(streamId, new TallyIncremented(1));
            await blue.SaveChangesAsync();
        }

        await using (var red = theStore.LightweightSession("red"))
        {
            red.Events.Append(streamId, new TallyIncremented(2), new TallyIncremented(4));
            await red.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        await using var query = theStore.QuerySession();
        var tally = await query.LoadAsync<GlobalTally>(streamId);

        tally.ShouldNotBeNull();

        // Sliced per tenant, the document would reflect only the group applied last -- so both numbers
        // would come up short rather than wrong in some exotic way.
        tally.EventCount.ShouldBe(3);
        tally.Total.ShouldBe(7);
    }

    /// <summary>
    /// The remaining shape, and the one that decides whether "harmless" is the whole answer. An event
    /// that is neither appended under a stream declared for the global aggregate nor one of the
    /// projection's own event types satisfies neither of GlobalEventAppenderDecorator.Matches's rules.
    /// This test asserts its own precondition -- that the tenant ids really do disagree -- and only then
    /// asks whether the daemon still folds the aggregate correctly.
    /// </summary>
    [Fact]
    public async Task an_unrelated_event_type_does_not_corrupt_the_global_aggregate()
    {
        ConfigureGlobalProjectionStore();

        var streamId = Guid.NewGuid();

        await using (var blue = theStore.LightweightSession("blue"))
        {
            blue.Events.StartStream<GlobalTally>(streamId, new TallyIncremented(1), new TallyIncremented(2),
                new TallyIncremented(4));
            await blue.SaveChangesAsync();
        }

        await using (var red = theStore.LightweightSession("red"))
        {
            red.Events.Append(streamId, new UnrelatedNoise("not part of the tally"));
            await red.SaveChangesAsync();
        }

        // THE PRECONDITION. Without it this passes vacuously if the decorator turns out to catch this
        // shape too, making it a duplicate of the first test rather than a probe of the gap. Skipping
        // this check is exactly the trap that made #4085's shared-suite coverage useless on two stores
        // (jasperfx#727).
        var tenantIds = await tenantIdsOnStreamAsync(streamId);
        tenantIds.Count.ShouldBe(2);
        tenantIds.ShouldContain(StorageConstants.DefaultTenantId);
        tenantIds.ShouldContain("red");

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        await using var query = theStore.QuerySession();
        var tally = await query.LoadAsync<GlobalTally>(streamId);

        tally.ShouldNotBeNull();

        // The three tally events all sit on the default tenant, so a per-tenant split leaves them in one
        // group regardless; the 'red' group holds only the event the projection ignores. If that group
        // can still overwrite the document, these come up short.
        tally.EventCount.ShouldBe(3);
        tally.Total.ShouldBe(7);
    }

    /// <summary>
    /// The distinct tenant_id values actually persisted for one stream, read from the table rather than
    /// inferred from what the test asked for -- because what the test asked for is precisely the thing
    /// in question.
    /// </summary>
    private async Task<List<string>> tenantIdsOnStreamAsync(Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var reader = await conn
            .CreateCommand(
                $"select distinct tenant_id from {theStore.Events.DatabaseSchemaName}.mt_events where stream_id = :stream order by 1")
            .With("stream", streamId, NpgsqlDbType.Uuid)
            .ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var ids = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }
}
