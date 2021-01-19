using System;
using Baseline.Dates;

namespace Marten.Events.Daemon
{
    public class DaemonSettings
    {
        public TimeSpan LeadingEdgeBuffer { get; set; } = 1.Seconds();

        public TimeSpan FetchingCooldown { get; set; } = 1.Seconds();

        // TODO -- maybe just bring back what was there before
        //public ExceptionHandling ExceptionHandling { get; } = new ExceptionHandling();
    }
}
