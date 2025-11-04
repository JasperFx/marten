using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.MultiTenancy;

public class using_for_tenant_with_side_effects_and_subscriptions : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public using_for_tenant_with_side_effects_and_subscriptions(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task try_to_append_with_for_tenant_in_projection()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add(new NumbersSubscription(), ProjectionLifecycle.Async);
            opts.Projections.Errors.SkipApplyErrors = false;
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        using var session = theStore.LightweightSession("green");
        session.Events.StartStream([new MTAEvent(), new MTBEvent(), new MTCEvent()]);
        await session.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        var events = await theSession.Events.QueryAllRawEvents().Where(x => x.AnyTenant()).ToListAsync();
        events.Count.ShouldBe(6);
    }

    [Fact]
    public async Task try_to_append_with_for_tenant_in_subscription()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.Subscribe(new NumberBatchSubscription());
            opts.Projections.Errors.SkipApplyErrors = false;
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        using var session = theStore.LightweightSession("green");
        session.Events.StartStream([new MTAEvent(), new MTBEvent(), new MTCEvent()]);
        await session.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        var events = await theSession.Events.QueryAllRawEvents().Where(x => x.AnyTenant()).ToListAsync();
        events.Count.ShouldBe(6);
    }
}


public class NumbersSubscription: EventProjection
{
    public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        if (e.TenantId != "blue")
        {
            operations.ForTenant("blue").Events.Append(e.StreamId, e.Data);

        }

        return ValueTask.CompletedTask;
    }
}

public class NumberBatchSubscription: ISubscription
{
    public Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller, IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        foreach (var @event in page.Events)
        {
            if (@event.TenantId != "blue")
            {
                operations.ForTenant("blue").Events.Append(@event.StreamId, @event.Data);

            }
        }

        return Task.FromResult(NullChangeListener.Instance);
    }
}
