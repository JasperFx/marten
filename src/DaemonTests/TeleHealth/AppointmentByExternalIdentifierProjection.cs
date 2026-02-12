using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;

namespace DaemonTests.TeleHealth;

public class AppointmentByExternalIdentifierProjection : MultiStreamProjection<AppointmentByExternalIdentifier, Guid>
{
    public AppointmentByExternalIdentifierProjection()
    {
        Options.CacheLimitPerTenant = 1000;

        Identity<AppointmentExternalIdentifierAssigned>(x => x.ExternalId);
    }

    public override async Task EnrichEventsAsync(SliceGroup<AppointmentByExternalIdentifier, Guid> group,
        IQuerySession querySession, CancellationToken cancellation)
    {
        await group
            .EnrichWith<Appointment>() // should be fetched from cache or from store
            .ForEvent<AppointmentExternalIdentifierAssigned>()
            .ForEntityId(x => x.AppointmentId)
            .EnrichAsync((slice, @event, appointment) =>
            {
                slice.ReplaceEvent(@event, new EnrichedExternalIdentifierAssigned(@event.Data, appointment));
            });
    }

    private sealed record EnrichedExternalIdentifierAssigned(AppointmentExternalIdentifierAssigned Assigned, Appointment Appointment);

    public override AppointmentByExternalIdentifier Evolve(AppointmentByExternalIdentifier snapshot, Guid id, IEvent e) =>
        e.Data switch
        {
            EnrichedExternalIdentifierAssigned enriched => new()
            {
                ExternalIdentifier = enriched.Assigned.ExternalId,
                AppointmentId = enriched.Assigned.AppointmentId,
                SpecialtyCode = enriched.Appointment.SpecialtyCode
            },
            _ => snapshot
        };
}

public class AppointmentByExternalIdentifier
{
    [Identity]
    public required Guid ExternalIdentifier { get; set; }
    public required Guid AppointmentId { get; set; }

    public required string SpecialtyCode { get; set; }
}
