using System.Diagnostics;

namespace Marten.Events.Projections.Async
{
    public class DebugDaemonLogger: TracingLogger
    {
        public DebugDaemonLogger() : base(x => Debug.WriteLine(x))
        {
        }
    }
}
