using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Projections;
using Microsoft.CodeAnalysis.Options;

namespace DaemonTests.TeleHealth;

public class BoardSummary
{
    public Guid Id { get; set; }
    public Board Board { get; set; }

    public Dictionary<Guid, ProviderShift> ActiveProviders { get; set; } = new();

    public Dictionary<Guid, AssignedAppointment> Assigned { get; set; } = new();

    public Dictionary<Guid, Appointment> Unassigned { get; set; } = new();
}


public record AssignedAppointment(Appointment Appointment, Provider Provider);

#region sample_BoardSummaryProjection

public class BoardSummaryProjection: MultiStreamProjection<BoardSummary, Guid>
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

#endregion

