// Lifted from src/DaemonTests/TeleHealth/{AppointmentDetailsProjection,BoardSummary}.cs
// (#4666 Phase B). Stage-2 multi-stream + enrichment projections — these read
// the stage-1 single-stream snapshots (Updated<Appointment> / Updated<Board> /
// Updated<ProviderShift>) plus reference docs (Patient, Provider, Specialty,
// RoutingReason, Board) and roll up richer enriched views.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.ScaleTesting.Domain;

// ---- AppointmentDetails -------------------------------------------------

public class AppointmentDetails
{
    public Guid Id { get; set; }

    public AppointmentDetails() { }

    public AppointmentDetails(Guid id)
    {
        Id = id;
    }

    public string PatientFirstName { get; set; } = string.Empty;
    public string PatientLastName { get; set; } = string.Empty;
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }

    public string SpecialtyCode { get; set; } = string.Empty;
    public string SpecialtyDescription { get; set; } = string.Empty;

    public string BoardName { get; set; } = string.Empty;
    public Guid? BoardId { get; set; }
    public string ProviderFirstName { get; set; } = string.Empty;
    public string ProviderLastName { get; set; } = string.Empty;
    public ProviderRole ProviderRole { get; set; }

    public DateTimeOffset Requested { get; set; }
    public DateTimeOffset? EstimatedTime { get; set; }
    public DateTimeOffset? CompletedTime { get; set; }
    public AppointmentStatus Status { get; set; }
    public string RoutingReasonCode { get; set; } = string.Empty;
    public string RoutingReasonDescription { get; set; } = string.Empty;
    public int RoutingReasonSeverity { get; set; }
}

public partial class AppointmentDetailsProjection: MultiStreamProjection<AppointmentDetails, Guid>
{
    public AppointmentDetailsProjection()
    {
        Options.CacheLimitPerTenant = 1000;

        Identity<Updated<Appointment>>(x => x.Entity.Id);
        Identity<IEvent<ProviderAssigned>>(x => x.StreamId);
        Identity<IEvent<AppointmentRouted>>(x => x.StreamId);

        // Synthetic event published from upstream projections so we can mirror
        // the simpler Appointment projection's delete decisions here.
        Identity<ProjectionDeleted<Appointment, Guid>>(x => x.Identity);
    }

    public override async Task EnrichEventsAsync(SliceGroup<AppointmentDetails, Guid> group, IQuerySession querySession,
        CancellationToken cancellation)
    {
        await group
            .EnrichWith<Specialty>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.Requirement!.SpecialtyCode)
            .AddReferences();

        await group
            .EnrichWith<Patient>()
            .ForEvent<Updated<Appointment>>()
            .ForEntityId(x => x.Entity.PatientId)
            .AddReferences();

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

        // RoutingReason is looked up by business key (ReasonCode), not document id,
        // so we use the EnrichUsingEntityQuery escape hatch + the slice cache.
        await group
            .EnrichWith<RoutingReason>()
            .ForEvent<AppointmentRouted>()
            .EnrichUsingEntityQuery<string>(async (slices, events, cache, ct) =>
            {
                var reasonCodes = events
                    .Select(e => e.Data.ReasonCode)
                    .Where(x => x.IsNotEmpty())
                    .Distinct()
                    .ToArray();
                if (reasonCodes.Length == 0) return;

                var missingCodes = cache != null
                    ? reasonCodes.Where(code => !cache.TryFind(code, out _)).ToList()
                    : reasonCodes.ToList();

                var localLookup = new Dictionary<string, RoutingReason>();
                if (missingCodes.Count > 0)
                {
                    var reasonsFromDb = await querySession
                        .Query<RoutingReason>()
                        .Where(r => r.Code.IsOneOf(missingCodes))
                        .Where(r => r.IsActive)
                        .ToListAsync(ct);

                    foreach (var reason in reasonsFromDb)
                    {
                        cache?.Store(reason.Code, reason);
                        localLookup[reason.Code] = reason;
                    }
                }

                // #4684 item 4: the escape-hatch business-key lookup — one batched query when
                // any codes miss the slice cache, zero when the cache fully covers the page
                Instrumentation.LookupCounters.Record(
                    nameof(AppointmentDetailsProjection) + ".RoutingReason",
                    missingCodes.Count > 0 ? 1 : 0, events.Count);

                foreach (var slice in slices)
                {
                    var codesInSlice = slice.Events()
                        .OfType<IEvent<AppointmentRouted>>()
                        .Select(x => x.Data.ReasonCode)
                        .Where(x => x.IsNotEmpty())
                        .Distinct()
                        .ToArray();

                    foreach (var code in codesInSlice)
                    {
                        if ((cache != null && cache.TryFind(code, out var reason)) ||
                            localLookup.TryGetValue(code, out reason))
                        {
                            slice.Reference(reason);
                        }
                    }
                }
            }, cancellation);
    }

    public override AppointmentDetails? Evolve(AppointmentDetails? snapshot, Guid id, IEvent e)
    {
        // Enrichment-added References<T> synthetic events sometimes arrive
        // before the snapshot-creating Updated<Appointment>. Default-init up
        // front so every case can safely write fields without an NRE.
        snapshot ??= new AppointmentDetails(id);

        switch (e.Data)
        {
            case AppointmentRequested requested:
                snapshot.SpecialtyCode = requested.SpecialtyCode;
                snapshot.PatientId = requested.PatientId;
                break;

            case Updated<Appointment> updated:
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

            case ProjectionDeleted<Appointment>:
                return null;
        }

        return snapshot;
    }
}

// ---- BoardSummary -------------------------------------------------------

public class BoardSummary
{
    public Guid Id { get; set; }
    public Board? Board { get; set; }

    public Dictionary<Guid, ProviderShift> ActiveProviders { get; set; } = new();
    public Dictionary<Guid, AssignedAppointment> Assigned { get; set; } = new();
    public Dictionary<Guid, Appointment> Unassigned { get; set; } = new();
}

public record AssignedAppointment(Appointment Appointment, Provider? Provider);

public partial class BoardSummaryProjection: MultiStreamProjection<BoardSummary, Guid>
{
    public BoardSummaryProjection()
    {
        Options.CacheLimitPerTenant = 100;

        Identity<Updated<Appointment>>(x => x.Entity.BoardId ?? Guid.Empty);
        Identity<Updated<Board>>(x => x.Entity.Id);
        Identity<Updated<ProviderShift>>(x => x.Entity.BoardId);
    }

    public override Task EnrichEventsAsync(SliceGroup<BoardSummary, Guid> group, IQuerySession querySession, CancellationToken cancellation)
    {
        return group.ReferencePeerView<Board>();
    }

    public override (BoardSummary, ActionType) DetermineAction(BoardSummary snapshot, Guid identity, IReadOnlyList<IEvent> events)
    {
        snapshot ??= new BoardSummary { Id = identity };
        if (events.TryFindReference<Board>(out var board))
        {
            snapshot.Board = board;
        }

        var shifts = events.AllReferenced<ProviderShift>().ToArray();
        foreach (var providerShift in shifts)
        {
            snapshot.ActiveProviders[providerShift.ProviderId] = providerShift;
            if (providerShift.AppointmentId.HasValue)
            {
                snapshot.Unassigned.Remove(providerShift.ProviderId);
            }
        }

        foreach (var appointment in events.AllReferenced<Appointment>())
        {
            if (appointment.ProviderId == null)
            {
                snapshot.Unassigned[appointment.Id] = appointment;
                snapshot.Assigned.Remove(appointment.Id);
            }
            else
            {
                snapshot.Unassigned.Remove(appointment.Id);
                var shift = shifts.FirstOrDefault(x => x.Id == appointment.ProviderId.Value);
                snapshot.Assigned[appointment.Id] = new AssignedAppointment(appointment, shift?.Provider);
            }
        }

        return (snapshot, ActionType.Store);
    }
}

// ---- AppointmentMetrics (custom IProjection) ---------------------------

public class AppointmentMetrics
{
    [Identity]
    public string SpecialtyCode { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AppointmentMetricsProjection: IProjection
{
    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        var groups = events
            .Where(e => e.Data is AppointmentRequested)
            .Select(e => (AppointmentRequested)e.Data)
            .GroupBy(r => r.SpecialtyCode)
            .ToArray();

        foreach (var group in groups)
        {
            var metrics = await operations.LoadAsync<AppointmentMetrics>(group.Key, cancellation)
                          ?? new AppointmentMetrics { SpecialtyCode = group.Key };
            metrics.Count += group.Count();
            operations.Store(metrics);
        }

        // #4684 item 4: one LoadAsync round-trip per specialty group in this invocation
        Instrumentation.LookupCounters.Record(nameof(AppointmentMetricsProjection),
            groups.Length, groups.Sum(g => g.Count()));
    }
}
