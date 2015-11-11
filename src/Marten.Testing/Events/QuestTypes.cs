using System;
using Marten.Events;

namespace Marten.Testing.Events
{
    public class Quest : IAggregate
    {
        public Guid Id { get; set; }
    }

    public class MembersJoined : IEvent
    {
        public Guid Id { get; set; }

        public string[] Members { get; set; }
    }

    public class MembersDeparted : IEvent
    {
        public Guid Id { get; set; }

        public string[] Members { get; set; }
    }
}