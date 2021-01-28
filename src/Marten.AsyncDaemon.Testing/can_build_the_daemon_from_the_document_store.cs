using Marten.Testing.Harness;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    [Collection("daemon")]
    public class can_build_the_daemon_from_the_document_store : OneOffConfigurationsContext
    {
        public can_build_the_daemon_from_the_document_store() : base("daemon")
        {
        }

        [Fact]
        public void can_build_it_out_with_no_custom_logging()
        {
            using var daemon = theStore.BuildProjectionDaemon();
        }
    }
}
