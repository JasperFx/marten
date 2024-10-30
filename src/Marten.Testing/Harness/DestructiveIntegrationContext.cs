using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Use this base class if a test fixture needs to do anything
    /// destructive to the default database schema
    /// </summary>
    public class DestructiveIntegrationContext: IntegrationContext
    {
        public DestructiveIntegrationContext(DefaultStoreFixture fixture) : base(fixture)
        {

        }

        protected override async Task fixtureSetup()
        {
            await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        }

        public override void Dispose()
        {
            base.Dispose();
            theStore.Advanced.Clean.CompletelyRemoveAllAsync().GetAwaiter().GetResult();


        }
    }
}
