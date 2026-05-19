using System;

namespace ModularConfigTests.SatelliteA;

public record OrderPlaced(Guid OrderId, decimal Amount);

public record OrderShipped(Guid OrderId);
