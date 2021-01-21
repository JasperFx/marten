using System;
using Baseline.Dates;

namespace Marten.Events.Daemon
{
    public class DaemonSettings
    {
        public TimeSpan LeadingEdgeBuffer { get; set; } = 1.Seconds();

        public TimeSpan SlowPollingTime { get; set; } = 1.Seconds();
        public TimeSpan FastPollingTime { get; set; } = 250.Milliseconds();

        // This is for the "safe harbor" timestamp where you assume that missing
        // events in the sequence are never coming in
        public TimeSpan StaleSequenceThreshold { get; set; } = 3.Seconds();
        public TimeSpan HealthCheckPollingTime { get; set; } = 5.Seconds();

        // TODO -- maybe just bring back what was there before
        //public ExceptionHandling ExceptionHandling { get; } = new ExceptionHandling();
    }
}
