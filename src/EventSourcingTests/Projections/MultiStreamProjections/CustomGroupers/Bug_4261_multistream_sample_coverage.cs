using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Projections.MultiStreamProjections.CustomGroupers;

/// <summary>
/// End-to-end coverage for the three MultiStreamProjection "aggregate id not on the event"
/// patterns documented in docs/events/projections/multi-stream-projections.md
/// (see https://github.com/JasperFx/marten/issues/4261).
///
/// The doc's snippet source file grouping_examples_for_unknown_ids.cs contains no [Fact]
/// tests — the classes only exist so the snippet extractor can find them. These tests
/// exercise the exact patterns from the docs under the async projection lifecycle.
///
/// Pattern 1 and Pattern 2 are expected to FAIL when the link event and the usage event
/// land in the same SaveChangesAsync batch — the scenario raised in the issue's gist
/// (https://gist.github.com/ghord/8ed794e27f2757d2a569ac1154b8bea6) and in discussion
/// https://github.com/JasperFx/marten/discussions/3615.
/// Pattern 3 should pass because the derived event carries the group key directly.
/// </summary>
public class Bug_4261_multistream_sample_coverage
{
    private readonly ITestOutputHelper _output;

    public Bug_4261_multistream_sample_coverage(ITestOutputHelper output)
    {
        _output = output;
    }

    // ───────────────────────── Pattern 1 ─────────────────────────

    [Fact]
    public async Task pattern1_async_same_batch_link_and_usage_is_applied_correctly()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p1_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P1.CustomerRegistered));
            opts.Events.AddEventType(typeof(P1.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P1.ShippingLabelCreated));

            opts.Projections.Add<P1.ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<P1.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-1";

        // Same-batch: register customer, start external-account stream with BOTH
        // the link event and a usage event in a single SaveChangesAsync.
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P1.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P1.CustomerLinkedToExternalAccount(customerId, externalAccountId),
                new P1.ShippingLabelCreated(externalAccountId));

            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P1.CustomerBillingMetrics>(customerId);

        // The shipping label should have been counted: the inline ExternalAccountLinkProjection
        // commits the lookup row before the async daemon processes the batch.
        doc.ShouldNotBeNull();
        doc.ShippingLabels.ShouldBe(1);
    }

    [Fact]
    public async Task pattern1_async_usage_before_link_in_separate_batches_works()
    {
        // Sanity baseline: when the link is committed first and the usage arrives
        // in a later batch, Pattern 1 should Just Work.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p1_seq_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P1.CustomerRegistered));
            opts.Events.AddEventType(typeof(P1.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P1.ShippingLabelCreated));

            opts.Projections.Add<P1.ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<P1.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-seq";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P1.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P1.CustomerLinkedToExternalAccount(customerId, externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(externalAccountId, new P1.ShippingLabelCreated(externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P1.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.ShippingLabels.ShouldBe(1);
    }

    // ───────────────────────── Pattern 2 ─────────────────────────

    [Fact]
    public async Task pattern2_async_same_batch_loses_usage_event_known_limitation()
    {
        // Locked-in regression for Pattern 2's known limitation.
        //
        // When the link event (CustomerLinkedToExternalAccount) and the usage event
        // (ShippingLabelCreated) land in the same async daemon batch, Pattern 2's grouper
        // — which queries CustomerBillingMetrics.LinkedExternalAccounts by containment —
        // finds no owner because the link has not yet been applied to the aggregate in
        // this batch cycle. The usage event is silently dropped.
        //
        // This is architecturally broken for same-batch ordering and the docs now call
        // this out explicitly. See Pattern 4 for the recommended batch-aware grouper.
        //
        // If a future engine change makes Pattern 2 work under same-batch ordering,
        // this test will fail and should be retired.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p2_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P2.CustomerRegistered));
            opts.Events.AddEventType(typeof(P2.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P2.ShippingLabelCreated));

            opts.Projections.Add<P2.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-1";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P2.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P2.CustomerLinkedToExternalAccount(customerId, externalAccountId),
                new P2.ShippingLabelCreated(externalAccountId));

            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P2.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.LinkedExternalAccounts.ShouldContain(externalAccountId);
        // Broken-by-design: the shipping label is dropped because the link hasn't
        // been applied to the aggregate in this batch cycle. Prefer Pattern 4.
        doc.ShippingLabels.ShouldBe(0);
    }

    [Fact]
    public async Task pattern2_async_link_in_earlier_batch_then_usage_works()
    {
        // Sanity baseline: when the link arrives first and is already applied to the
        // projection, Pattern 2's containment query in a later batch finds the owner.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p2_seq_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P2.CustomerRegistered));
            opts.Events.AddEventType(typeof(P2.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P2.ShippingLabelCreated));

            opts.Projections.Add<P2.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-seq";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P2.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P2.CustomerLinkedToExternalAccount(customerId, externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(externalAccountId, new P2.ShippingLabelCreated(externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P2.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.LinkedExternalAccounts.ShouldContain(externalAccountId);
        doc.ShippingLabels.ShouldBe(1);
    }

    // ───────────────────────── Pattern 4 (batch-aware grouper) ─────────────────────────

    [Fact]
    public async Task pattern4_async_same_batch_link_and_usage_works()
    {
        // Pattern 4's batch-aware grouper consults the current batch's events
        // to pick up link events that share the same daemon cycle as the usage
        // event. This is the recommended pattern when link+usage events can
        // appear in a single SaveChangesAsync.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p4_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P4.CustomerRegistered));
            opts.Events.AddEventType(typeof(P4.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P4.ShippingLabelCreated));

            opts.Projections.Add<P4.ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<P4.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-p4-same";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P4.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P4.CustomerLinkedToExternalAccount(customerId, externalAccountId),
                new P4.ShippingLabelCreated(externalAccountId));

            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P4.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.ShippingLabels.ShouldBe(1);
    }

    [Fact]
    public async Task pattern4_async_link_in_earlier_batch_then_usage_works()
    {
        // Baseline: Pattern 4 must also handle the case where the link event
        // was committed in a prior batch. The DB fallback covers that.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p4_seq_net{Environment.Version.Major}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType(typeof(P4.CustomerRegistered));
            opts.Events.AddEventType(typeof(P4.CustomerLinkedToExternalAccount));
            opts.Events.AddEventType(typeof(P4.ShippingLabelCreated));

            opts.Projections.Add<P4.ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<P4.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var externalAccountId = "acct-p4-seq";

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(customerId.ToString(), new P4.CustomerRegistered(customerId, "Alice"));
            session.Events.StartStream(externalAccountId,
                new P4.CustomerLinkedToExternalAccount(customerId, externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(externalAccountId, new P4.ShippingLabelCreated(externalAccountId));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P4.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.ShippingLabels.ShouldBe(1);
    }

    // ───────────────────────── Pattern 3 ─────────────────────────

    [Fact]
    public async Task pattern3_async_same_batch_is_correct_by_design()
    {
        // Pattern 3 keeps the grouping key (CustomerId) on the terminal event itself,
        // so same-batch ordering cannot create a race.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"b4261_p3_net{Environment.Version.Major}";
            opts.Events.AddEventType(typeof(P3.ShipmentBilled));

            opts.Projections.Add<P3.CustomerBillingProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var customerId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(shipmentId, new P3.ShipmentBilled(customerId, shipmentId, 5));
            await session.SaveChangesAsync();
        }

        await daemon.WaitForNonStaleData(10.Seconds());

        await using var query = store.QuerySession();
        var doc = await query.LoadAsync<P3.CustomerBillingMetrics>(customerId);

        doc.ShouldNotBeNull();
        doc.Shipments.ShouldBe(1);
        doc.Items.ShouldBe(5);
    }

    // ───────────────────────── Test fixtures (per-pattern) ─────────────────────────

    public static class P1
    {
        public interface IExternalAccountEvent { string ExternalAccountId { get; } }

        public record CustomerRegistered(Guid CustomerId, string DisplayName);
        public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);
        public record ShippingLabelCreated(string ExternalAccountId) : IExternalAccountEvent;

        public class ExternalAccountLink
        {
            public required string Id { get; set; }
            public required Guid CustomerId { get; set; }
        }

        public class ExternalAccountLinkProjection : SingleStreamProjection<ExternalAccountLink, string>
        {
            public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
            {
                link.Id = e.ExternalAccountId;
                link.CustomerId = e.CustomerId;
            }
        }

        public class ExternalAccountToCustomerGrouper : IAggregateGrouper<Guid>
        {
            public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
            {
                var usageEvents = events.Where(e => e.Data is IExternalAccountEvent).ToList();
                if (usageEvents.Count == 0) return;

                var externalIds = usageEvents
                    .Select(e => ((IExternalAccountEvent)e.Data).ExternalAccountId)
                    .Distinct()
                    .ToList();

                var links = await session.Query<ExternalAccountLink>()
                    .Where(x => externalIds.Contains(x.Id))
                    .Select(x => new { x.Id, x.CustomerId })
                    .ToListAsync();

                var map = links.ToDictionary(x => x.Id, x => x.CustomerId);

                foreach (var e in usageEvents)
                {
                    var externalId = ((IExternalAccountEvent)e.Data).ExternalAccountId;
                    if (map.TryGetValue(externalId, out var customerId))
                        grouping.AddEvent(customerId, e);
                }
            }
        }

        public class CustomerBillingMetrics
        {
            public Guid Id { get; set; }
            public int ShippingLabels { get; set; }
        }

        public class CustomerBillingProjection : MultiStreamProjection<CustomerBillingMetrics, Guid>
        {
            public CustomerBillingProjection()
            {
                Identity<CustomerRegistered>(e => e.CustomerId);
                CustomGrouping(new ExternalAccountToCustomerGrouper());
            }

            public CustomerBillingMetrics Create(CustomerRegistered e) => new() { Id = e.CustomerId };
            public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _) => view.ShippingLabels++;
        }
    }

    public static class P2
    {
        public record CustomerRegistered(Guid CustomerId, string DisplayName);
        public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);
        public record ShippingLabelCreated(string ExternalAccountId);

        public class CustomerBillingMetrics
        {
            public Guid Id { get; set; }
            public List<string> LinkedExternalAccounts { get; set; } = new();
            public int ShippingLabels { get; set; }
        }

        public class CustomerBillingProjection : MultiStreamProjection<CustomerBillingMetrics, Guid>
        {
            public CustomerBillingProjection()
            {
                Identity<CustomerRegistered>(e => e.CustomerId);
                Identity<CustomerLinkedToExternalAccount>(e => e.CustomerId);

                CustomGrouping(async (session, events, grouping) =>
                {
                    var labelEvents = events.OfType<IEvent<ShippingLabelCreated>>().ToList();
                    if (labelEvents.Count == 0) return;

                    var externalIds = labelEvents
                        .Select(x => x.Data.ExternalAccountId)
                        .Distinct()
                        .ToList();

                    var owners = await session.Query<CustomerBillingMetrics>()
                        .Where(x => x.LinkedExternalAccounts.Any(id => externalIds.Contains(id)))
                        .Select(x => new { x.Id, x.LinkedExternalAccounts })
                        .ToListAsync();

                    var map = owners
                        .SelectMany(o => o.LinkedExternalAccounts.Select(id => new { ExternalId = id, CustomerId = o.Id }))
                        .ToDictionary(x => x.ExternalId, x => x.CustomerId);

                    foreach (var e in labelEvents)
                    {
                        if (map.TryGetValue(e.Data.ExternalAccountId, out var customerId))
                            grouping.AddEvent(customerId, e);
                    }
                });
            }

            public CustomerBillingMetrics Create(CustomerRegistered e) => new() { Id = e.CustomerId };

            public void Apply(CustomerBillingMetrics view, CustomerLinkedToExternalAccount e)
            {
                if (!view.LinkedExternalAccounts.Contains(e.ExternalAccountId))
                    view.LinkedExternalAccounts.Add(e.ExternalAccountId);
            }

            public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _) => view.ShippingLabels++;
        }
    }

    public static class P3
    {
        public record ShipmentBilled(Guid CustomerId, Guid ShipmentId, int UniqueItems);

        public class CustomerBillingMetrics
        {
            public required Guid Id { get; set; }
            public required int Shipments { get; set; }
            public required int Items { get; set; }
        }

        public class CustomerBillingProjection : MultiStreamProjection<CustomerBillingMetrics, Guid>
        {
            public CustomerBillingProjection()
            {
                Identity<ShipmentBilled>(e => e.CustomerId);
            }

            public CustomerBillingMetrics Create(ShipmentBilled e)
                => new() { Id = e.CustomerId, Shipments = 1, Items = e.UniqueItems };

            public void Apply(CustomerBillingMetrics view, ShipmentBilled e)
            {
                view.Shipments++;
                view.Items += e.UniqueItems;
            }
        }
    }

    public static class P4
    {
        public record CustomerRegistered(Guid CustomerId, string DisplayName);
        public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);
        public record ShippingLabelCreated(string ExternalAccountId);

        public class CustomerBillingMetrics
        {
            public Guid Id { get; set; }
            public int ShippingLabels { get; set; }
        }

        public class ExternalAccountLink
        {
            public required string Id { get; set; }
            public required Guid CustomerId { get; set; }
        }

        public class ExternalAccountLinkProjection : SingleStreamProjection<ExternalAccountLink, string>
        {
            public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
            {
                link.Id = e.ExternalAccountId;
                link.CustomerId = e.CustomerId;
            }
        }

        /// <summary>
        /// Batch-aware grouper: consults in-batch link events first, then falls back
        /// to a DB lookup for any external ids still unresolved. Maintains a small
        /// grouper-instance cache to avoid repeated DB round-trips across daemon cycles.
        /// </summary>
        public class BatchAwareExternalAccountGrouper : IAggregateGrouper<Guid>
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> _cache = new();

            public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
            {
                var materialized = events as IReadOnlyCollection<IEvent> ?? events.ToList();

                var labelEvents = materialized.OfType<IEvent<ShippingLabelCreated>>().ToList();
                if (labelEvents.Count == 0) return;

                // 1) Pick up any link events that share THIS batch.
                foreach (var linkEvent in materialized.OfType<IEvent<CustomerLinkedToExternalAccount>>())
                {
                    _cache[linkEvent.Data.ExternalAccountId] = linkEvent.Data.CustomerId;
                }

                // 2) For any external ids still unresolved, query the lookup table.
                var unresolved = labelEvents
                    .Select(x => x.Data.ExternalAccountId)
                    .Distinct()
                    .Where(id => !_cache.ContainsKey(id))
                    .ToList();

                if (unresolved.Count > 0)
                {
                    var links = await session.Query<ExternalAccountLink>()
                        .Where(x => unresolved.Contains(x.Id))
                        .Select(x => new { x.Id, x.CustomerId })
                        .ToListAsync();

                    foreach (var link in links)
                    {
                        _cache[link.Id] = link.CustomerId;
                    }
                }

                // 3) Route each usage event to the matching customer id.
                foreach (var e in labelEvents)
                {
                    if (_cache.TryGetValue(e.Data.ExternalAccountId, out var customerId))
                    {
                        grouping.AddEvent(customerId, e);
                    }
                }
            }
        }

        public class CustomerBillingProjection : MultiStreamProjection<CustomerBillingMetrics, Guid>
        {
            public CustomerBillingProjection()
            {
                Identity<CustomerRegistered>(e => e.CustomerId);
                CustomGrouping(new BatchAwareExternalAccountGrouper());
            }

            public CustomerBillingMetrics Create(CustomerRegistered e) => new() { Id = e.CustomerId };

            public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _) => view.ShippingLabels++;
        }
    }
}
