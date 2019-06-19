using System;

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

    public class EventA
    {
        public string Name { get; set; }
    }

    public class EventB
    {
        public string Name { get; set; }
    }

    public class EventC
    {
        public string Name { get; set; }
    }

    public class EventD
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }
}
