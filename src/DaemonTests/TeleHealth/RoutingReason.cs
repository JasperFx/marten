using System;

namespace DaemonTests.TeleHealth;

public class RoutingReason
{
    public Guid Id { get; set; }
    public string Code { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }

    public int Severity { get; set; }
}
