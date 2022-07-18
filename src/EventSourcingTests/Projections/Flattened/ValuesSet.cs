using System;

namespace EventSourcingTests.Projections.Flattened;

public class ValuesSet
{
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }

    public string Name { get; set; }
    public Guid Guid { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
}
