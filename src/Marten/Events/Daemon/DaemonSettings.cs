using System;
using Baseline.Dates;

namespace Marten.Events.Daemon
{
    public class DaemonSettings
    {
        public TimeSpan StaleSequenceThreshold { get; set; } = 3.Seconds();

        public TimeSpan SlowPollingTime { get; set; } = 1.Seconds();
        public TimeSpan FastPollingTime { get; set; } = 250.Milliseconds();


        public TimeSpan HealthCheckPollingTime { get; set; } = 5.Seconds();

        // TODO -- maybe just bring back what was there before
        //public ExceptionHandling ExceptionHandling { get; } = new ExceptionHandling();

        public DaemonMode Mode { get; set; } = DaemonMode.Disabled;
    }

    public enum DaemonMode
    {
        Disabled,
        Solo,
        HotCold,
        Distributed
    }
}
