using System;
using Marten.Events;
using MemoryPack;

namespace Marten.MemoryPack.Tests;

// Binary event types — opted in via attribute. With
// opts.Events.UseMemoryPackSerializer() set as the store-wide fallback,
// these resolve to the MemoryPack serializer on registration.
[BinaryEvent]
[MemoryPackable]
public partial record TripStarted(Guid TripId, string DriverName, DateTimeOffset StartedAt);

[BinaryEvent]
[MemoryPackable]
public partial record PassengerPickedUp(Guid TripId, string PassengerName, DateTimeOffset PickedUpAt);

[BinaryEvent]
[MemoryPackable]
public partial record TripEnded(Guid TripId, DateTimeOffset EndedAt, decimal FareAmount);

// JSON event — coexists in the same store and is appended without
// MemoryPack on the wire. Used by the mixed-stream test.
public record TripCommentAdded(Guid TripId, string Comment, DateTimeOffset At);
