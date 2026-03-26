using System;
using MemoryPack;

namespace Marten.MemoryPack.Tests;

[MemoryPackable]
public partial class TripStarted
{
    public Guid TripId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
}

[MemoryPackable]
public partial class PassengerPickedUp
{
    public Guid TripId { get; set; }
    public string PassengerName { get; set; } = string.Empty;
    public DateTimeOffset PickedUpAt { get; set; }
}

[MemoryPackable]
public partial class TripEnded
{
    public Guid TripId { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public double EndLatitude { get; set; }
    public double EndLongitude { get; set; }
    public decimal FareAmount { get; set; }
}

// This type intentionally does NOT have [MemoryPackable] for validation testing
public class NonMemoryPackableEvent
{
    public string Name { get; set; } = string.Empty;
}

// Simple aggregate for projection testing
[MemoryPackable]
public partial class Trip
{
    public Guid Id { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public decimal FareAmount { get; set; }
    public int PassengerCount { get; set; }
    public bool IsActive { get; set; }

    public void Apply(TripStarted e)
    {
        DriverName = e.DriverName;
        StartedAt = e.StartedAt;
        IsActive = true;
    }

    public void Apply(PassengerPickedUp e)
    {
        PassengerCount++;
    }

    public void Apply(TripEnded e)
    {
        EndedAt = e.EndedAt;
        FareAmount = e.FareAmount;
        IsActive = false;
    }
}
