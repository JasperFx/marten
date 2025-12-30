using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;

namespace DaemonTests.TeleHealth;

internal interface BoardStateEvent;

public record BoardOpened(string Name, DateOnly Date, DateTimeOffset Opened, string[] StateCodes, string[] SpecialtyCodes) : BoardStateEvent;
public record BoardFinished(DateTimeOffset Timestamp) : BoardStateEvent;
public record BoardClosed(DateTimeOffset Timestamp, string Reason) : BoardStateEvent;
public record ShiftAdded(Guid ShiftId);
public record AlertRaised(string AlertCode);
public record AlertCleared(string AlertCode);
public record ShiftDropped(Guid ShiftId);

// This will be a write model
public class Board
{
    public Board()
    {
    }

    public Board(BoardOpened opened)
    {
        Name = opened.Name;
        Activated = opened.Opened;
        Date = opened.Date;

        SpecialtyCodes = opened.SpecialtyCodes;
        StateCodes = opened.StateCodes;
    }

    public void Apply(BoardFinished finished) => Finished = finished.Timestamp;

    public void Apply(BoardClosed closed)
    {
        Closed = closed.Timestamp;
        CloseReason = closed.Reason;
    }

    public void Apply(ShiftAdded added) => ActiveShifts.Fill(added.ShiftId);

    public void Apply(ShiftDropped dropped) => ActiveShifts.Remove(dropped.ShiftId);

    public void Apply(AlertRaised alert)
    {
        AlertCodes = AlertCodes.Concat([alert.AlertCode]).Distinct().ToArray();
    }

    public void Apply(AlertCleared cleared)
    {
        AlertCodes = AlertCodes.Where(x => x != cleared.AlertCode).ToArray();
    }

    public Guid Id { get; set; }
    public string Name { get; }
    public DateTimeOffset Activated { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset? Closed { get; set; }

    public string[] AlertCodes { get; set; } = [];
    public string[] StateCodes { get; set; } = [];
    public string[] SpecialtyCodes { get; set; } = [];

    public string CloseReason { get; private set; }
    public List<Guid> ActiveShifts { get; set; } = new();
}
