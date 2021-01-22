namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class TripEnded : IDayEvent
    {
        public int Day { get; set; }
        public string State { get; set; }
    }
}
