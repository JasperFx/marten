using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using static EventSourcingTests.Projections.MultiStreamProjections.CustomGroupers.GroupingForUnknownIdsByLookupExample;

namespace EventSourcingTests.Projections.MultiStreamProjections.CustomGroupers;

public class GroupingForUnknownIdsByLookupExample: OneOffConfigurationsContext
{
    #region sample_external-account-link-events

    public interface IExternalAccountEvent
    {
        string ExternalAccountId { get; }
    }

    public record CustomerRegistered(Guid CustomerId, string DisplayName);

    public record CustomerLinkedToExternalAccount(Guid CustomerId, string ExternalAccountId);

    public record ShippingLabelCreated(string ExternalAccountId): IExternalAccountEvent;

    public record TrackingItemSeen(string ExternalAccountId, string Mode): IExternalAccountEvent;

    #endregion

    #region sample_external-account-link

    public class ExternalAccountLink
    {
        public required string Id { get; set; } // ExternalAccountId
        public required Guid CustomerId { get; set; }
    }

    public class ExternalAccountLinkProjection: SingleStreamProjection<ExternalAccountLink, string>
    {
        public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
        {
            link.Id = e.ExternalAccountId;
            link.CustomerId = e.CustomerId;
        }
    }

    #endregion

    #region sample_external-account-link-grouper

    public class ExternalAccountToCustomerGrouper: IAggregateGrouper<Guid>
    {
        public async Task Group(IQuerySession session, IEnumerable<IEvent> events, IEventGrouping<Guid> grouping)
        {
            var usageEvents = events
                .Where(e => e.Data is IExternalAccountEvent)
                .ToList();

            if (usageEvents.Count == 0) return;

            var externalIds = usageEvents
                .Select(e => ((IExternalAccountEvent)e.Data).ExternalAccountId)
                .Distinct()
                .ToList();

            var links = await session.Query<ExternalAccountLink>()
                .Where(x => externalIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CustomerId })
                .ToListAsync();

            var map = links.ToDictionary(x => x.Id, x => x.CustomerId!);

            foreach (var @event in usageEvents)
            {
                var externalId = ((IExternalAccountEvent)@event.Data).ExternalAccountId;

                if (map.TryGetValue(externalId, out var customerId))
                    grouping.AddEvent(customerId, @event);
            }
        }
    }

    #endregion

    #region sample_external-account-link-multi-stream-projection

    public class CustomerBillingMetrics
    {
        public Guid Id { get; set; }
        public int ShippingLabels { get; set; }
        public int TrackingEvents { get; set; }
        public HashSet<string> ModesSeen { get; set; } = [];
    }

    public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
    {
        public CustomerBillingProjection()
        {
            // notice you can mix custom grouping and Identity<T>(...)
            Identity<CustomerRegistered>(e => e.CustomerId);
            CustomGrouping(new ExternalAccountToCustomerGrouper());
        }

        public CustomerBillingMetrics Create(CustomerRegistered e)
            => new() { Id = e.CustomerId };

        public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _)
            => view.ShippingLabels++;

        public void Apply(CustomerBillingMetrics view, TrackingItemSeen e)
        {
            view.TrackingEvents++;
            view.ModesSeen.Add(e.Mode);
        }
    }

    #endregion

    public GroupingForUnknownIdsByLookupExample()
    {
        StoreOptions(opts =>
        {
            #region sample_external-account-link-lookup-registration

            opts.Projections.Add<ExternalAccountLinkProjection>(ProjectionLifecycle.Inline);
            opts.Projections.Add<CustomerBillingProjection>(ProjectionLifecycle.Async);

            #endregion
        });
    }
}

public class GroupingForUnknownIdsByBookKeepingIdListExample: OneOffConfigurationsContext
{
    #region sample_external-account-link-id-list-grouper

    public class CustomerBillingMetrics
    {
        public Guid Id { get; set; }
        public List<string> LinkedExternalAccounts { get; set; } = new();

        public int ShippingLabels { get; set; }
    }

    public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
    {
        public CustomerBillingProjection()
        {
            Identity<CustomerRegistered>(e => e.CustomerId);
            Identity<CustomerLinkedToExternalAccount>(e => e.CustomerId);

            CustomGrouping(async (session, events, grouping) =>
            {
                var labelEvents = events
                    .OfType<IEvent<ShippingLabelCreated>>()
                    .ToList();

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

        public CustomerBillingMetrics Create(CustomerRegistered e)
            => new() { Id = e.CustomerId };

        public void Apply(CustomerBillingMetrics view, CustomerLinkedToExternalAccount e)
        {
            if (!view.LinkedExternalAccounts.Contains(e.ExternalAccountId))
                view.LinkedExternalAccounts.Add(e.ExternalAccountId);
        }

        public void Apply(CustomerBillingMetrics view, ShippingLabelCreated _)
            => view.ShippingLabels++;
    }
    #endregion
}

public class GroupingForUnknownIdsFatEventExample: OneOffConfigurationsContext
{
    #region sample_shipment-events

    public record ShipmentStarted(string ExternalAccountId, Guid CustomerId);

    public record ItemScanned(string ItemId);

    public record ShipmentCompleted;

    #region sample_shipment-events-billed

    public record ShipmentBilled(Guid CustomerId, Guid ShipmentId, int UniqueItems);

    #endregion

    #endregion

    #region sample_shipment

    public class Shipment
    {
        public required string ExternalAccountId { get; set; }
        public required Guid CustomerId { get; set; }
        public HashSet<string> Items { get; set; } = [];

        public Shipment Create(ShipmentStarted e) => new()
        {
            ExternalAccountId = e.ExternalAccountId, CustomerId = e.CustomerId
        };

        public void Apply(ItemScanned e) => Items.Add(e.ItemId);
    }

    #endregion

    #region sample_shipment-events-multi-stream-projection

    public class CustomerBillingMetrics
    {
        public required Guid Id { get; set; } // CustomerId
        public required int Shipments { get; set; }
        public required int Items { get; set; }
    }

    public class CustomerBillingProjection: MultiStreamProjection<CustomerBillingMetrics, Guid>
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

    #endregion
}
