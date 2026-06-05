// Lifted from src/DaemonTests/TeleHealth/{Patient,Providers,RoutingReason,Specialty}.cs (#4666 Phase A).
using Marten.Schema;

namespace Marten.ScaleTesting.Domain;

public class Patient
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

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
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public ProviderRole Role { get; set; }

    public List<Licensing> Licensing { get; set; } = [];
}

public class RoutingReason
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Severity { get; set; }
}

public class Specialty
{
    [Identity]
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
