using System;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public class DaemonSettings
    {
        public TimeSpan LeadingEdgeBuffer { get; set; } = 1.Seconds();

        public TimeSpan FetchingCooldown { get; set; } = 1.Seconds();
    }
}