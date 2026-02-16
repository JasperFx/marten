using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;

namespace DaemonTests.TeleHealth;

#region sample_AppointmentDetailsProjection

public class AppointmentDetailsProjection: MultiStreamProjection<AppointmentDetails, Guid>
{
    public AppointmentDetailsProjection()
    {
        Options.CacheLimitPerTenant = 1000;

        Identity<Updated<Appointment>>(x => x.Entity.Id);
        Identity<IEvent<ProviderAssigned>>(x => x.StreamId);
        Identity<IEvent<AppointmentRouted>>(x => x.StreamId);

        // This is a synthetic event published from upstream projections to identify
        // which projected Appointment documents were deleted as part of the current event range
        // so we can keep this richer model mirroring the simpler Appointment projection
        Identity<ProjectionDeleted<Appointment, Guid>>(x => x.Identity);
    }

    public override async Task EnrichEventsAsync(SliceGroup<AppointmentDetails, Guid> group, IQuerySession querySession,
        CancellationToken cancellation)
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

        #region sample_using_forevent_addreferences

        // Look up and apply provider information
        await group
            .EnrichWith<Provider>()
            .ForEvent<ProviderAssigned>()
            .ForEntityId(x => x.ProviderId)
            .AddReferences();

        // Look up and apply Board information that matches the events being projected
        await group
            .EnrichWith<Board>()
            .ForEvent<AppointmentRouted>()
            .ForEntityId(x => x.BoardId)
            .AddReferences();

        #endregion

        #region sample_using_enrich_using_entity_query

        // Enrich RoutingReason documents based on a business key (ReasonCode),
        // not on the document id. This example also demonstrates how to use
        // the provided cache to avoid repeated database queries.
        await group
            .EnrichWith<RoutingReason>()
            .ForEvent<AppointmentRouted>()
            .EnrichUsingEntityQuery<string>(async (slices, events, cache, ct) =>
            {
                // Collect all distinct reason codes across the incoming events
                var reasonCodes = events
                    .Select(e => e.Data.ReasonCode)
                    .Where(x => x.IsNotEmpty())
                    .Distinct()
                    .ToArray();

                // Nothing to enrich if no reason codes are present
                if (reasonCodes.Length == 0)
                {
                    return;
                }

                // Try to resolve RoutingReason documents from the cache first
                var missingCodes = reasonCodes.Where(code => !cache.TryFind(code, out _)).ToList();

                // Only query the database for codes that are not yet cached
                if (missingCodes.Count > 0)
                {
                    var reasonsFromDb = await querySession
                        .Query<RoutingReason>()
                        .Where(r => r.Code.IsOneOf(missingCodes))
                        .Where(r => r.IsActive)
                        .ToListAsync(ct);

                    // Store fetched documents in the cache for reuse
                    foreach (var reason in reasonsFromDb)
                    {
                        cache.Store(reason.Code, reason);
                    }
                }

                // Apply the resolved RoutingReason references per slice
                foreach (var slice in slices)
                {
                    // Snapshot the events first, referencing modifies slice state
                    var codesInSlice = slice.Events()
                        .OfType<IEvent<AppointmentRouted>>()
                        .Select(x => x.Data.ReasonCode)
                        .Where(x => x.IsNotEmpty())
                        .Distinct()
                        .ToArray();

                    foreach (var code in codesInSlice)
                    {
                        if (cache.TryFind(code, out var reason))
                        {
                            slice.Reference(reason);
                        }
                    }
                }
            }, cancellation);

        #endregion

    }

    #region sample_AppointmentDetails_Evolve

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

            case References<RoutingReason> reason:
                snapshot.RoutingReasonCode = reason.Entity.Code;
                snapshot.RoutingReasonDescription = reason.Entity.Description;
                snapshot.RoutingReasonSeverity = reason.Entity.Severity;
                break;

            // The matching projection for Appointment was deleted
            // so we'll delete this enriched projection as well
            // ProjectionDeleted<TDoc> is a synthetic event that Marten
            // itself publishes from the upstream projections and available
            // to downstream projections
            case ProjectionDeleted<Appointment>:
                return null;
        }

        return snapshot;
    }

    #endregion
}

#endregion

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
    public string RoutingReasonCode { get; set; }
    public string RoutingReasonDescription { get; set; }
    public int RoutingReasonSeverity { get; set; }
}
