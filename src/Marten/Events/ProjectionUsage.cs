using System;

namespace Marten.Events
{
    public class ProjectionUsage
    {
        public ProjectionTiming timing { get; set; }
        public string name { get; set; }
        public string event_name { get; set; }
        public ProjectionType type { get; set; }

        public Guid id { get; set; }
    }
}