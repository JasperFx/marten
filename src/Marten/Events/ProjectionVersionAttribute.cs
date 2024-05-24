using System;

namespace Marten.Events;

/// <summary>
/// Alternative to mark aggregation projections as being versioned to
/// opt into Marten's blue/green deployment support for projections
/// </summary>
/// <param name="version"></param>
[AttributeUsage(AttributeTargets.Class)]
public class ProjectionVersionAttribute(uint version): Attribute
{
    public uint Version { get; } = version;
}
