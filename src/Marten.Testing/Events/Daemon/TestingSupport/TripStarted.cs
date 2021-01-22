namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class TripStarted : IDayEvent
    {
        public int Day { get; set; }
    }
}
