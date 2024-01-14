using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Please use this context for testing defects that require a special DocumentStore setup
    /// </summary>
    [Collection("bugs")]
    public class BugIntegrationContext: OneOffConfigurationsContext
    {
        public BugIntegrationContext() : base("bugs")
        {
        }
    }
}
