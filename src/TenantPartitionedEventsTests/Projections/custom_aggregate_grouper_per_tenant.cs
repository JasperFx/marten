using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c deferred — pin <see cref="IAggregateGrouper{TId}"/> under
/// <c>UseTenantPartitionedEvents</c>. The grouper's <c>Group</c> method receives
/// an <see cref="IQuerySession"/> that MUST be tenant-scoped to the slice it's
/// processing — otherwise the lookup query inside the grouper would leak across
/// tenant partitions and the resulting aggregate would mix events from sibling
/// tenants.
///
/// <para>
/// Pattern under test: an inline <see cref="ExternalAccountLinkProjection"/>
/// builds a per-tenant lookup of (external-account-id → customer-id), and a
/// <see cref="CustomerBillingProjection"/> uses a custom grouper that queries
/// that lookup to route <see cref="ShippingLabelCreated"/> events to the right
/// customer. Each tenant must see only its own external-account → customer
/// links, even though the grouper's <c>IQuerySession</c> is provided by the
/// projection framework rather than the user.
/// </para>
///
/// <para>
/// Own-store because <see cref="ExternalAccountLinkProjection"/> +
/// <see cref="CustomerBillingProjection"/> register two projections that
/// would change the shared fixture's projection set for every sibling test.
/// </para>
/// </summary>
public class custom_aggregate_grouper_per_tenant : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_grouper_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<CustomerRegistered>();
            opts.Events.AddEventType<CustomerLinkedToExternalAccount>();
            opts.Events.AddEventType<ShippingLabelCreated>();

            // Inline lookup: external-account-id -> customer-id (string-keyed
            // since ExternalAccountId is the natural identity).
            opts.Projections.Add<ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            // Inline billing: uses the custom grouper to fan ShippingLabelCreated
            // out to the matching customer via the link table.
            opts.Projections.Add<CustomerBillingProjection>(ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task grouper_lookup_resolves_only_to_same_tenant_link_docs()
    {
        // Two tenants. Both happen to use the SAME external-account-id string
        // ("ACME-PRO") but point to DIFFERENT customers. If the grouper's
        // IQuerySession leaks across partitions, alpha's lookup might find
        // beta's link doc and route beta's shipping events to alpha's customer
        // (or vice versa). The pin: each tenant's billing metrics reflect ONLY
        // its own customer's shipping events.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        const string sharedExternalId = "ACME-PRO";
        var alphaCustomer = Guid.NewGuid();
        var betaCustomer = Guid.NewGuid();

        // alpha: register customer, link to ACME-PRO, send 3 shipping labels.
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaCustomer, new CustomerRegistered(alphaCustomer, "Alpha Inc"));
            session.Events.Append(alphaCustomer, new CustomerLinkedToExternalAccount(alphaCustomer, sharedExternalId));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession("alpha"))
        {
            var labelStream = Guid.NewGuid();
            session.Events.StartStream(labelStream,
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId));
            await session.SaveChangesAsync();
        }

        // beta: register a DIFFERENT customer, link to the SAME external id,
        // send 5 shipping labels.
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaCustomer, new CustomerRegistered(betaCustomer, "Beta LLC"));
            session.Events.Append(betaCustomer, new CustomerLinkedToExternalAccount(betaCustomer, sharedExternalId));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession("beta"))
        {
            var labelStream = Guid.NewGuid();
            session.Events.StartStream(labelStream,
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId),
                new ShippingLabelCreated(sharedExternalId));
            await session.SaveChangesAsync();
        }

        // Read each tenant's billing doc using a tenant-scoped query. The
        // billing metric is multi-tenanted (per AllDocumentsAreMultiTenanted),
        // so each tenant sees only its own row keyed by its own customer id.
        await using var alphaQuery = _store.QuerySession("alpha");
        var alphaBilling = await alphaQuery.LoadAsync<CustomerBillingMetrics>(alphaCustomer);
        alphaBilling.ShouldNotBeNull("alpha's grouper must have routed alpha's labels to alpha's customer");
        alphaBilling!.ShippingLabels.ShouldBe(3,
            "alpha appended 3 labels — beta's 5 must not bleed in via shared external id");

        await using var betaQuery = _store.QuerySession("beta");
        var betaBilling = await betaQuery.LoadAsync<CustomerBillingMetrics>(betaCustomer);
        betaBilling.ShouldNotBeNull("beta's grouper must have routed beta's labels to beta's customer");
        betaBilling!.ShippingLabels.ShouldBe(5,
            "beta appended 5 labels — alpha's 3 must not bleed in via shared external id");
    }

    [Fact]
    public async Task tenant_A_billing_doc_invisible_from_tenant_B_session()
    {
        // Sibling pin: the billing doc itself is partitioned per tenant (it's
        // a multi-tenanted doc). Querying tenant B with tenant A's customer id
        // must return null — no doc visibility across tenant slots.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaCustomer = Guid.NewGuid();
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaCustomer, new CustomerRegistered(alphaCustomer, "Alpha Inc"));
            session.Events.Append(alphaCustomer, new CustomerLinkedToExternalAccount(alphaCustomer, "EXT-A"));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession("alpha"))
        {
            var labelStream = Guid.NewGuid();
            session.Events.StartStream(labelStream, new ShippingLabelCreated("EXT-A"));
            await session.SaveChangesAsync();
        }

        // Pin from alpha's own session: doc exists.
        await using (var alphaQuery = _store.QuerySession("alpha"))
        {
            (await alphaQuery.LoadAsync<CustomerBillingMetrics>(alphaCustomer)).ShouldNotBeNull();
        }

        // Pin from beta's session: alpha's customer id finds nothing — the
        // billing doc is in alpha's tenant slot, beta's tenant slot is empty.
        await using (var betaQuery = _store.QuerySession("beta"))
        {
            (await betaQuery.LoadAsync<CustomerBillingMetrics>(alphaCustomer))
                .ShouldBeNull("alpha's billing doc must not be visible to beta — tenant slot isolation");
        }
    }
}

public record CustomerRegistered(Guid CustomerId, string DisplayName);
public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);
public record ShippingLabelCreated(string ExternalAccountId);

public class ExternalAccountLink
{
    [Identity]
    public string Id { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
}

public partial class ExternalAccountLinkProjection : MultiStreamProjection<ExternalAccountLink, string>
{
    public ExternalAccountLinkProjection()
    {
        Name = "ExternalAccountLink";
        // Key the link doc by ExternalAccountId — independent of the underlying
        // event stream's Guid identity. Keeping the link projection as a
        // MultiStream avoids forcing the whole store to StreamIdentity.AsString
        // for what is purely a lookup table.
        Identity<CustomerLinkedToExternalAccount>(e => e.ExternalAccountId);
    }

    public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
    {
        link.Id = e.ExternalAccountId;
        link.CustomerId = e.CustomerId;
    }
}

public class CustomerBillingMetrics
{
    public Guid Id { get; set; }
    public int ShippingLabels { get; set; }
}

public partial class CustomerBillingProjection : MultiStreamProjection<CustomerBillingMetrics, Guid>
{
    public CustomerBillingProjection()
    {
        Name = "CustomerBilling";
        Identity<CustomerRegistered>(e => e.CustomerId);
        CustomGrouping(new ExternalAccountToCustomerGrouper());
    }

    public CustomerBillingMetrics Create(CustomerRegistered e) => new() { Id = e.CustomerId };

    public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _) => view.ShippingLabels++;
}

/// <summary>
/// The grouper of interest — it queries the (per-tenant) link table via the
/// supplied <see cref="IQuerySession"/>. Under partitioning, that session MUST
/// be tenant-scoped to the slice currently being processed.
/// </summary>
public class ExternalAccountToCustomerGrouper : IAggregateGrouper<Guid>
{
    public async Task Group(IQuerySession session, IReadOnlyList<IEvent> events, IEventGrouping<Guid> grouping)
    {
        var labelEvents = events.OfType<IEvent<ShippingLabelCreated>>().ToList();
        if (labelEvents.Count == 0) return;

        var externalIds = labelEvents.Select(e => e.Data.ExternalAccountId).Distinct().ToList();

        // This is the tenancy-critical query — must hit ONLY the current
        // session's tenant partition, otherwise sibling tenants' links would
        // be returned and the grouping would route to the wrong customer id.
        var links = await session.Query<ExternalAccountLink>()
            .Where(x => externalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CustomerId })
            .ToListAsync();

        var map = links.ToDictionary(x => x.Id, x => x.CustomerId);

        foreach (var e in labelEvents)
        {
            if (map.TryGetValue(e.Data.ExternalAccountId, out var customerId))
                grouping.AddEvent(customerId, e);
        }
    }
}
