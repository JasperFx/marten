using System;

namespace Marten.Events
{
    public class Event
    {
        public Guid Id { get; set; }
        public object Body { get; set; }
    }
}
