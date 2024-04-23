namespace DaemonTests.TestingSupport;

public class TripEnded : IDayEvent
{
    public int Day { get; set; }
    public string State { get; set; }
}