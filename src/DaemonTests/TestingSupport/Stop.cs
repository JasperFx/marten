using System;

namespace DaemonTests.TestingSupport;

public class Stop
{
    public TimeOnly Time { get; set; }
    public string State { get; set; }
    public int Duration { get; set; }
}
