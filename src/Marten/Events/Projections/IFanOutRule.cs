using System;
using System.Collections.Generic;
using JasperFx.Events;

namespace Marten.Events.Projections;

/// <summary>
///     When does the fanout rule apply?
/// </summary>
public enum FanoutMode
{
    /// <summary>
    ///     Do the "fan out" of events *before* doing any grouping
    /// </summary>
    BeforeGrouping,

    /// <summary>
    ///     Do the "fan out" of events *after* grouping
    /// </summary>
    AfterGrouping
}

public interface IFanOutRule
{
    Type OriginatingType { get; }

    FanoutMode Mode { get; }
    void Apply(List<IEvent> events);
}
