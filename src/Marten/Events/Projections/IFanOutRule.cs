using System;
using System.Collections.Generic;

namespace Marten.Events.Projections
{
    /// <summary>
    /// When does the fanout rule apply?
    /// </summary>
    public enum FanoutMode
    {
        /// <summary>
        /// Do the "fan out" of events *before* doing any grouping
        /// </summary>
        BeforeGrouping,

        /// <summary>
        /// Do the "fan out" of events *after* grouping
        /// </summary>
        AfterGrouping
    }

    public interface IFanOutRule
    {
        void Apply(List<IEvent> events);
        Type OriginatingType { get; }

        FanoutMode Mode { get; }
    }
}
