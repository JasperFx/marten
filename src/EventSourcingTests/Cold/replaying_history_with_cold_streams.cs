#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

public class replaying_history_with_cold_streams: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private void configure(Action<StoreOptions> projections, bool cleanAll = true)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            projections(opts);
        }, cleanAll);
    }

    private async Task<string> startCart(params string[] items)
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Cart>(key,
            new object[] { new CartOpened("anne") }.Concat(items.Select(x => new ItemAdded(x))).ToArray());
        await session.SaveChangesAsync();
        return key;
    }

    /// <summary>A cart whose first two events went cold after the daemon projected them, and one warm event.</summary>
    private async Task<string> coldCartProjectedByTheDaemonAsync()
    {
        var key = await startCart("apple");

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        await using var session = theStore.LightweightSession();
        session.Events.Append(key, new ItemAdded("pear"));
        await session.SaveChangesAsync();

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
        }

        return key;
    }

    private async Task<int?> countOf(string key)
    {
        await using var session = theStore.QuerySession();
        return (await session.LoadAsync<CartItemCount>(key))?.Count;
    }

    [Fact]
    public async Task a_rebuild_is_refused_before_it_tears_anything_down()
    {
        configure(opts => opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Async));
        var key = await coldCartProjectedByTheDaemonAsync();
        (await countOf(key)).ShouldBe(2);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var e = await Should.ThrowAsync<ColdStreamReplayException>(
            () => daemon.RebuildProjectionAsync<CartItemCount>(CancellationToken.None));
        e.ProjectionName.ShouldBe(typeof(CartItemCount).Name);

        (await countOf(key)).ShouldBe(2);
    }

    [Fact]
    public async Task a_projection_that_ignores_cold_streams_is_rebuilt_from_the_warm_events()
    {
        configure(opts =>
        {
            var projection = new CartItemCountProjection();
            projection.Options.IgnoreColdStreams();
            opts.Projections.Add(projection, ProjectionLifecycle.Async);
        });
        var key = await coldCartProjectedByTheDaemonAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CartItemCount>(CancellationToken.None);

        (await countOf(key)).ShouldBe(1);
    }

    [Fact]
    public async Task a_projection_registered_after_streams_went_cold_does_not_start_from_the_beginning()
    {
        configure(_ => { });
        var key = await startCart("apple");
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 2);

        configure(opts => opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Async),
            cleanAll: false);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var paused = await pausedAgentAsync(daemon);
        paused.Failure!.RootExceptionType.ShouldContain(nameof(ColdStreamReplayException));
        (await countOf(key)).ShouldBeNull();
    }

    [Fact]
    public async Task a_composite_that_ignores_cold_streams_is_rebuilt()
    {
        configure(opts => opts.Projections.CompositeProjectionFor("Carts", composite =>
        {
            composite.Add<CartItemCountProjection>();
            composite.Options.IgnoreColdStreams();
        }));
        var key = await coldCartProjectedByTheDaemonAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync("Carts", CancellationToken.None);

        (await countOf(key)).ShouldBe(1);
    }

    [Fact]
    public async Task a_composite_whose_member_alone_ignores_cold_streams_is_refused()
    {
        configure(opts => opts.Projections.CompositeProjectionFor("Carts", composite =>
        {
            var member = new CartItemCountProjection();
            member.Options.IgnoreColdStreams();
            composite.Add(member);
        }));
        var key = await coldCartProjectedByTheDaemonAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var e = await Should.ThrowAsync<ColdStreamReplayException>(
            () => daemon.RebuildProjectionAsync("Carts", CancellationToken.None));
        e.ProjectionName.ShouldBe("Carts");

        (await countOf(key)).ShouldBe(2);
    }

    [Fact]
    public async Task a_rebuild_without_cold_streams_is_not_affected()
    {
        configure(opts => opts.Projections.Add(new CartItemCountProjection(), ProjectionLifecycle.Async));
        var key = await startCart("apple", "pear");

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CartItemCount>(CancellationToken.None);

        (await countOf(key)).ShouldBe(2);
    }

    private static async Task<ISubscriptionAgent> pausedAgentAsync(IProjectionDaemon daemon)
    {
        var timeout = DateTimeOffset.UtcNow.Add(30.Seconds());
        while (DateTimeOffset.UtcNow < timeout)
        {
            var paused = daemon.CurrentAgents().FirstOrDefault(x => x.Status == AgentStatus.Paused);
            if (paused != null) return paused;
            await Task.Delay(100);
        }

        throw new TimeoutException("No agent was paused");
    }
}
