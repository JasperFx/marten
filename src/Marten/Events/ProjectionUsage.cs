using System;

namespace Marten.Events
{
    [Obsolete("No longer used. Will be removed in version 4.")]
    public class ProjectionUsage
    {
        public ProjectionTiming timing { get; set; }
        public string name { get; set; }
        public string event_name { get; set; }
        public ProjectionType type { get; set; }

        public override string ToString()
        {
            return $"Projection {name} ({type}) for Event {event_name} executed {timing}";
        }
    }
}
