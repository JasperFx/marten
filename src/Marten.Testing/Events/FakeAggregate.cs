using System;
using Marten.Events;

namespace Marten.Testing.Events
{
    public class FakeAggregate
    {
        public Guid Id { get; set; }

        public string[] ANames;
        public string[] BNames;
        public string[] CNames;
        public string[] DNames;
    }

    public class EventA : IEvent
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }

    public class EventB : IEvent
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }

    public class EventC : IEvent
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }


    public class EventD : IEvent
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }


}