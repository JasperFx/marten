using System;

namespace Marten.Events
{
    public class Event
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public object Data { get; set; }
    }
}
