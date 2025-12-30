using Marten.Schema;

namespace DaemonTests.TeleHealth;

// Document
public class Specialty
{
    [Identity]
    public string Code { get; set; }
    public string Description { get; set; }
}




