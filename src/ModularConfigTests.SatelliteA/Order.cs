using System;

namespace ModularConfigTests.SatelliteA;

public class Order
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public bool IsShipped { get; set; }
}
