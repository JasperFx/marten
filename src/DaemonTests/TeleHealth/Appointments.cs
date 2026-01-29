using System;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace DaemonTests.TeleHealth;

public record AppointmentRequested(Guid PatientId, string StateCode, string SpecialtyCode);
public record AppointmentRouted(Guid BoardId);
public record ProviderAssigned( Guid ProviderId);
public record AppointmentStarted;
public record AppointmentCompleted;
public record AppointmentEstimated(DateTimeOffset Time);

public record AppointmentCancelled;

public enum AppointmentStatus
{
    Requested,
    Scheduled,
    Started,
    Completed
}

public class Appointment
{
    public Guid Id { get; set; }

    public int Version { get; set; }
    public DateTimeOffset Created { get; set; }
    public string SpecialtyCode { get; set; }

    public Licensing Requirement { get; set; }
    public AppointmentStatus Status { get; set; }
    public Guid? ProviderId { get; set; }
    public DateTimeOffset? EstimatedTime { get; set; }
    public Guid? BoardId { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Completed { get; set; }
}

#region sample_AppointmentProjection

public class AppointmentProjection: SingleStreamProjection<Appointment, Guid>
{
    public AppointmentProjection()
    {
        // Make sure this is turned on!
        Options.CacheLimitPerTenant = 1000;
    }

    public override Appointment Evolve(Appointment snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case AppointmentRequested requested:
                snapshot = new Appointment()
                {
                    Status = AppointmentStatus.Requested,
                    Requirement = new Licensing(requested.SpecialtyCode, requested.StateCode),
                    PatientId = requested.PatientId,
                    Created = e.Timestamp,
                    SpecialtyCode = requested.SpecialtyCode
                };
                break;

            case AppointmentRouted routed:
                snapshot.BoardId = routed.BoardId;
                break;

            case ProviderAssigned assigned:
                snapshot.ProviderId = assigned.ProviderId;
                break;

            case AppointmentEstimated estimated:
                snapshot.Status = AppointmentStatus.Scheduled;
                snapshot.EstimatedTime = estimated.Time;
                break;

            case AppointmentStarted:
                snapshot.Status = AppointmentStatus.Started;
                snapshot.Started = e.Timestamp;
                break;

            case AppointmentCompleted:
                snapshot.Status = AppointmentStatus.Completed;
                snapshot.Completed = e.Timestamp;
                break;

            case AppointmentCancelled:
                return null;
        }

        return snapshot;
    }
}

#endregion

