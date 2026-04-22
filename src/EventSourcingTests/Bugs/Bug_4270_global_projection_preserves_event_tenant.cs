using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Regression coverage for https://github.com/JasperFx/marten/issues/4270.
///
/// When a SingleStreamProjection is registered with AddGlobalProjection on a
/// store with TenancyStyle.Conjoined, the events the projection consumes should
/// preserve the tenant id under which they were appended. Prior to the fix,
/// GlobalEventAppenderDecorator overwrote every IEvent.TenantId on the in-flight
/// stream with the default tenant id before projections ran, so
/// Create(IEvent&lt;T&gt;) / Apply(IEvent&lt;T&gt;, TDoc) convention methods that read
/// @event.TenantId saw "*DEFAULT*" instead of the real tenant under which the
/// events had been appended. The projection document still stores single-tenanted
/// (that is the documented purpose of AddGlobalProjection), but the in-flight
/// IEvent seen by convention methods should reflect the original session tenant.
/// </summary>
public class Bug_4270_global_projection_preserves_event_tenant : OneOffConfigurationsContext
{
    public Bug_4270_global_projection_preserves_event_tenant()
    {
        Bug4270TenantCapturer.Reset();
    }

    [Fact]
    public async Task event_tenant_id_is_preserved_in_create_apply_convention_methods()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.AddGlobalProjection(
                new Bug4270OrderSummaryProjection(),
                ProjectionLifecycle.Inline);
        });

        var streamKey = $"order-{Guid.NewGuid():N}";
        const string tenant = "acme";

        await using var session = theStore.LightweightSession(tenant);
        session.Events.StartStream(streamKey,
            new Bug4270OrderCreated("cust-1", 99.50m),
            new Bug4270OrderShipped());
        await session.SaveChangesAsync();

        // Inside Create / Apply, @event.TenantId should be the original tenant
        // under which the events were appended, NOT the default tenant.
        Bug4270TenantCapturer.TenantIdsSeen.ShouldContain(tenant);
        Bug4270TenantCapturer.TenantIdsSeen.ShouldNotContain(StorageConstants.DefaultTenantId);

        // The projected document should be stored single-tenanted (i.e., in the default
        // tenant), but should have captured the original tenant on the document itself.
        await using var query = theStore.QuerySession();
        var doc = await query.LoadAsync<Bug4270OrderSummary>(streamKey);
        doc.ShouldNotBeNull();
        doc.TenantId.ShouldBe(tenant);
        doc.Status.ShouldBe("Shipped");
    }

    [Fact]
    public async Task projection_storage_is_still_single_tenanted_after_fix()
    {
        // AddGlobalProjection is documented to store the aggregate (and its stream)
        // under the default tenant — that is the whole point of "global within
        // conjoined tenancy". Assert that preserving the original tenant on
        // in-flight IEvent objects doesn't accidentally break that guarantee.
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.AddGlobalProjection(
                new Bug4270OrderSummaryProjection(),
                ProjectionLifecycle.Inline);
        });

        var streamKey = $"order-{Guid.NewGuid():N}";
        const string tenant = "acme";

        await using var session = theStore.LightweightSession(tenant);
        session.Events.StartStream(streamKey, new Bug4270OrderCreated("cust-2", 10m));
        await session.SaveChangesAsync();

        // Projection document should be reachable from the default tenant query
        // session, even though we appended under "acme".
        await using var defaultQuery = theStore.QuerySession();
        var doc = await defaultQuery.LoadAsync<Bug4270OrderSummary>(streamKey);
        doc.ShouldNotBeNull();

        // And Create() should have seen the ORIGINAL tenant "acme" on the event,
        // not "*DEFAULT*".
        doc.TenantId.ShouldBe(tenant);
    }
}

// ─────────────────────────── fixtures ───────────────────────────

public record Bug4270OrderCreated(string CustomerId, decimal TotalAmount);
public record Bug4270OrderShipped;

public class Bug4270OrderSummary
{
    public string Id { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string CustomerId { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = default!;
}

public class Bug4270OrderSummaryProjection : SingleStreamProjection<Bug4270OrderSummary, string>
{
    public static Bug4270OrderSummary Create(IEvent<Bug4270OrderCreated> @event)
    {
        Bug4270TenantCapturer.TenantIdsSeen.Add(@event.TenantId!);
        return new Bug4270OrderSummary
        {
            Id = @event.StreamKey!,
            TenantId = @event.TenantId!,
            CustomerId = @event.Data.CustomerId,
            TotalAmount = @event.Data.TotalAmount,
            Status = "Created"
        };
    }

    public static Bug4270OrderSummary Apply(IEvent<Bug4270OrderShipped> @event, Bug4270OrderSummary item)
    {
        Bug4270TenantCapturer.TenantIdsSeen.Add(@event.TenantId!);
        item.Status = "Shipped";
        return item;
    }
}

internal static class Bug4270TenantCapturer
{
    public static ConcurrentBag<string> TenantIdsSeen { get; private set; } = new();

    public static void Reset() => TenantIdsSeen = new ConcurrentBag<string>();
}
