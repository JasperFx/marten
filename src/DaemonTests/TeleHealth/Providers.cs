using System;
using System.Collections.Generic;

namespace DaemonTests.TeleHealth;


public record Licensing(string SpecialtyCode, string StateCode);

public enum ProviderRole
{
    Physician,
    PhysicianAssistant,
    Nurse
}

public class Provider
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public ProviderRole Role { get; set; }

    public List<Licensing> Licensing { get; set; } = [];
}
