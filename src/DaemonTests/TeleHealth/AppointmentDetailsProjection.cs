using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;

namespace DaemonTests.TeleHealth;



public class AppointmentDetailsProjection : MultiStreamProjection<AppointmentDetails, Guid>
{
    public AppointmentDetailsProjection()
    {
        Options.CacheLimitPerTenant = 1000;

        Identity<Updated<Appointment>>(x => x.Entity.Id);
        Identity<IEvent<ProviderAssigned>>(x => x.StreamId);
        Identity<IEvent<AppointmentRouted>>(x => x.StreamId);
    }

    public override async Task EnrichEventsAsync(SliceGroup<AppointmentDetails, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        // Look up and apply specialty information from the document store
        // Specialty is just reference data stored as a document in Marten
        await group
            .EnrichWith<Specialty>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.Requirement.SpecialtyCode)
            .AddReferences();

        // Also reference data (for now)
        await group
            .EnrichWith<Patient>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.PatientId)
            .AddReferences();

        // Look up and apply provider information
        await group
            .EnrichWith<Provider>()
            .ForEvent<ProviderAssigned>()
            .ForEntityId(x => x.ProviderId)
            .AddReferences();

        await group
            .EnrichWith<Board>()
            .ForEvent<AppointmentRouted>()
            .ForEntityId(x => x.BoardId)
            .AddReferences();

    }

    public override AppointmentDetails Evolve(AppointmentDetails snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AppointmentRequested requested:
                snapshot ??= new AppointmentDetails(e.StreamId);
                snapshot.SpecialtyCode = requested.SpecialtyCode;
                snapshot.PatientId = requested.PatientId;
                break;

            // This is an upstream projection. Triggering off of a synthetic
            // event that Marten publishes from the early stage
            // to this projection running in a secondary stage
            case Updated<Appointment> updated:
                snapshot ??= new AppointmentDetails(updated.Entity.Id);
                snapshot.Status = updated.Entity.Status;
                snapshot.EstimatedTime = updated.Entity.EstimatedTime;
                snapshot.SpecialtyCode = updated.Entity.SpecialtyCode;
                break;

            case References<Patient> patient:
                snapshot.PatientFirstName = patient.Entity.FirstName;
                snapshot.PatientLastName = patient.Entity.LastName;
                break;

            case References<Specialty> specialty:
                snapshot.SpecialtyCode = specialty.Entity.Code;
                snapshot.SpecialtyDescription = specialty.Entity.Description;
                break;

            case References<Provider> provider:
                snapshot.ProviderId = provider.Entity.Id;
                snapshot.ProviderFirstName = provider.Entity.FirstName;
                snapshot.ProviderLastName = provider.Entity.LastName;
                break;

            case References<Board> board:
                snapshot.BoardName = board.Entity.Name;
                snapshot.BoardId = board.Entity.Id;
                break;
        }

        return snapshot;
    }
}

public class AppointmentDetails
{
    public Guid Id { get; set; }

    public AppointmentDetails(Guid id)
    {
        Id = id;
    }

    public string PatientFirstName { get; set; }
    public string PatientLastName { get; set; }
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }

    public string SpecialtyCode { get; set; }
    public string SpecialtyDescription { get; set; }

    public string BoardName { get; set; }
    public Guid? BoardId { get; set; }
    public string ProviderFirstName { get; set; }
    public string ProviderLastName { get; set; }
    public ProviderRole ProviderRole { get; set; }

    public DateTimeOffset Requested { get; set; }
    public DateTimeOffset? EstimatedTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
    public AppointmentStatus Status { get; set; }
}
