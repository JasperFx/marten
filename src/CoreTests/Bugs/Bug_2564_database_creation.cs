using Marten.Testing.Harness;

namespace CoreTests.Bugs;

public class Bug_2564_database_creation : BugIntegrationContext
{

}

public sealed class Dbo
{
    public string Id { get; }


    public Dbo(string id)
    {
        Id = id;
    }
}
