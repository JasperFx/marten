using System;

namespace Marten.Events.Projections.Async
{
    public class ConsoleDaemonLogger : TracingLogger
    {
        public ConsoleDaemonLogger() : base(Console.WriteLine)
        {
        }
    }
}