using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Please use this context for bug tests
    /// </summary>
    [Collection("bugs")]
    public class BugIntegrationContext: OneOffConfigurationsContext
    {
        public BugIntegrationContext() : base("bugs")
        {
        }
    }
}
