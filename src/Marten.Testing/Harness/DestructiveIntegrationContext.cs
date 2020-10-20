using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Use this base class if a test fixture needs to do anything
    /// destructive to the default database schema
    /// </summary>
    [Collection("integration")]
    public class DestructiveIntegrationContext: IntegrationContext
    {
        public DestructiveIntegrationContext(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
        }

        public override void Dispose()
        {
            theStore.Advanced.Clean.CompletelyRemoveAll();
            base.Dispose();

        }
    }
}
