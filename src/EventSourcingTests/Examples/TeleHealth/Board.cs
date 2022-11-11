using System;
using System.Collections.Generic;

namespace EventSourcingTests.Examples.TeleHealth;

public interface IBoardEvent
{
    Guid BoardId { get; }
}

public class Board
{
    private Board()
    {
    }

    public Board(BoardOpened opened)
    {
        Name = opened.Name;
        Activated = opened.Opened;
        Date = opened.Date;
    }

    public void Apply(BoardFinished finished)
    {
        Finished = finished.Timestamp;
    }

    public void Apply(BoardClosed closed)
    {
        Closed = closed.Timestamp;
        CloseReason = closed.Reason;
    }

    public Guid Id { get; set; }
    public string Name { get; private set; }
    public DateTimeOffset Activated { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset? Closed { get; set; }

    public string CloseReason { get; private set; }
}

internal interface BoardStateEvent{}

public record BoardOpened(string Name, DateOnly Date, DateTimeOffset Opened) : BoardStateEvent;

public record BoardFinished(DateTimeOffset Timestamp) : BoardStateEvent;

public record BoardClosed(DateTimeOffset Timestamp, string Reason) : BoardStateEvent;

// Make this the target of a ViewProjection
public class BoardView
{
    public DateOnly Date { get; set; }

    public bool Active => Closed == null;

    public DateTimeOffset Opened { get; set; }
    public DateTimeOffset? Closed { get; set; }

    public DateTimeOffset? Deactivated { get; set; }

    public string CloseReason { get; set; }

    public string Name { get; set; }

    public Guid Id { get; set; }

    public IList<BoardAppointment> Appointments { get; set; } = new List<BoardAppointment>();
    public IList<BoardProvider> Providers { get; set; } = new List<BoardProvider>();

    public int ReadyCount { get; set; }
    public int RequestedCount { get; set; }
    public int ActiveCount { get; set; }
    public int CompletedCount { get; set; }
    public int ReadyProviders { get; set; }
    public int BusyProviders { get; set; }
    public int InactiveProviders { get; set; }
}

public class BoardAppointment
{
    public Guid AppointmentId { get; set; }
    public string PatientName { get; set; }
    public DateTimeOffset OriginalEstimatedTime { get; set; }
    public DateTimeOffset CurrentEstimatedTime { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTimeOffset Requested { get; set; }
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public Guid? ProviderId { get; set; }
}

public enum ProviderStatus
{
    Available,
    WithPatient,
    Charting,
    Unavailable
}

public class BoardProvider
{
    public Guid Id { get; set; }
    public Guid? AppointmentId { get; set; }
    public ProviderStatus Status { get; set; }
    public string Name { get; set; }
}
