using System;
using Baseline;
using Marten.Events.Projections.Async.ErrorHandling;

namespace Marten.Events.Projections.Async
{
    public class DaemonSettings
    {
        public TimeSpan LeadingEdgeBuffer { get; set; } = 1.Seconds();

        public TimeSpan FetchingCooldown { get; set; } = 1.Seconds();

        public ExceptionHandling ExceptionHandling { get; } = new ExceptionHandling();
    }
}