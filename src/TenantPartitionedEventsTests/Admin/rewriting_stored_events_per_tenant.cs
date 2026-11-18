#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

public class rewriting_stored_events_per_tenant: PartitionedStoreContext
{
    public record ParcelSent(string To);

    protected override string SchemaPrefix => "tp_rw";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<ParcelSent>();
        opts.EventGraph.AddRewriteTransform<ParcelSent>("upper", e => e with { To = e.To.ToUpperInvariant() });
    }

    [Fact]
    public async Task every_tenants_events_are_rewritten_although_they_share_sequence_numbers()
    {
        string[] tenants = ["alpha", "beta", "gamma"];
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        foreach (var tenant in tenants)
        {
            await using var session = Store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new ParcelSent($"{tenant} 1"), new ParcelSent($"{tenant} 2"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // One event per page, so every page boundary falls between two tenants' same-numbered events
        var result = await Store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.Transform<ParcelSent>(e => e with { To = e.To.ToUpperInvariant() });
            x.BatchSize = 1;
        }, TestContext.Current.CancellationToken);

        result.Databases.Single().Rewritten.ShouldBe(6);

        foreach (var tenant in tenants)
        {
            await using var query = Store.QuerySession(tenant);
            var events = await query.Events.QueryAllRawEvents().ToListAsync(TestContext.Current.CancellationToken);
            events.Select(e => ((ParcelSent)e.Data).To).OrderBy(x => x)
                .ShouldBe([$"{tenant.ToUpperInvariant()} 1", $"{tenant.ToUpperInvariant()} 2"]);
        }
    }

    [Fact]
    public async Task one_tenant_is_rewritten_alone_and_the_others_keep_their_rows()
    {
        string[] tenants = ["alpha", "beta"];
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        foreach (var tenant in tenants)
        {
            await using var session = Store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new ParcelSent($"{tenant} 1"), new ParcelSent($"{tenant} 2"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await Store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.Transform<ParcelSent>(e => e with { To = e.To.ToUpperInvariant() });
            x.ForTenant("beta");
            x.BatchSize = 1;
        }, TestContext.Current.CancellationToken);

        result.Databases.Single().Visited.ShouldBe(2);
        result.Databases.Single().Rewritten.ShouldBe(2);
        result.Databases.Single().Cursor.TenantId.ShouldBe("beta");

        await using var alpha = Store.QuerySession("alpha");
        (await alpha.Events.QueryAllRawEvents().ToListAsync(TestContext.Current.CancellationToken))
            .Select(e => ((ParcelSent)e.Data).To).OrderBy(x => x).ShouldBe(["alpha 1", "alpha 2"]);

        await using var beta = Store.QuerySession("beta");
        (await beta.Events.QueryAllRawEvents().ToListAsync(TestContext.Current.CancellationToken))
            .Select(e => ((ParcelSent)e.Data).To).OrderBy(x => x).ShouldBe(["BETA 1", "BETA 2"]);
    }

    // jasperfx#1215: through IEventStore.RewriteStoredEventsAsync, a run for the whole database goes through its
    // tenants one after another, because their sequences repeat. Each tenant gets its own ceiling and cursor.
    [Fact]
    public async Task a_job_run_for_the_whole_database_goes_tenant_by_tenant_with_its_own_ceiling_and_cursor()
    {
        string[] tenants = ["alpha", "beta"];
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        foreach (var tenant in tenants)
        {
            await using var session = Store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new ParcelSent($"{tenant} 1"), new ParcelSent($"{tenant} 2"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // beta gets one more, so the two tenants' ceilings differ
        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream(Guid.NewGuid(), new ParcelSent("beta 3"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var database = (IEventDatabase)Store.Storage.Database;
        var definition = new EventRewriteDefinition
        {
            Name = "upper" + Guid.NewGuid().ToString("N")[..8],
            Transforms = ["upper"],
            BatchSize = 1
        };

        var run = await EventRewriteRun.FromMarksAsync(database, definition, TestContext.Current.CancellationToken);
        var totals = await ((IEventStore)Store).RewriteStoredEventsAsync(run, TestContext.Current.CancellationToken);

        totals.Select(x => x.TenantId).ShouldBe(["alpha", "beta"]);
        totals.Select(x => x.Rewritten).ShouldBe([2L, 3L]);
        totals.ShouldAllBe(x => x.Completed);

        (await database.FetchEventRangeMarkAsync(run.CeilingKey, "alpha", TestContext.Current.CancellationToken))
            .ShouldBe(2);
        (await database.FetchEventRangeMarkAsync(run.CeilingKey, "beta", TestContext.Current.CancellationToken))
            .ShouldBe(3);
        (await database.FetchEventRangeMarkAsync(run.CursorKey, "alpha", TestContext.Current.CancellationToken))
            .ShouldBe(2);
        (await database.FetchEventRangeMarkAsync(run.CursorKey, "beta", TestContext.Current.CancellationToken))
            .ShouldBe(3);

        foreach (var tenant in tenants)
        {
            await using var query = Store.QuerySession(tenant);
            var events = await query.Events.QueryAllRawEvents().ToListAsync(TestContext.Current.CancellationToken);
            events.ShouldAllBe(e => ((ParcelSent)e.Data).To == ((ParcelSent)e.Data).To.ToUpperInvariant());
        }

        // Run again from the marks: every tenant is complete, so nothing goes through the transform twice
        var again = await ((IEventStore)Store).RewriteStoredEventsAsync(
            await EventRewriteRun.FromMarksAsync(database, definition, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        again.ShouldAllBe(x => x.Visited == 0 && x.Completed);
    }
}
