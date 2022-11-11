using System;
using Marten.Schema;

namespace EventSourcingTests.Examples.TeleHealth;

public record AppointmentRequested(string PatientName);

public record AppointmentRouted(string BoardName);

public record ProviderAssigned(Guid ProviderId);

public record ProviderJoined(Guid ProviderId, Guid ShiftId, string BoardId);

public record AppointmentScheduled
{
}

public record AppointmentStarted
{
}

public record AppointmentFinished
{
}

public record ChartingFinished
{
}

public record ChartingStarted
{
}

public record ProviderReady
{
}

public class Appointment
{
    public Guid Id { get; set; }
}

public class ProviderShift
{
    [Identity] public Guid ShiftId { get; set; }
    public Guid ProviderId { get; set; }
    public string BoardId { get; set; }
}

public enum AppointmentStatus
{
    Requested,
    Scheduled,
    Ready,
    Started,
}
