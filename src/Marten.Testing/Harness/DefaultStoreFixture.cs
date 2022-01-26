using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Util;
using Weasel.Core;
using Xunit;

namespace Marten.Testing.Harness
{
    public class DefaultStoreFixture: IAsyncLifetime
    {
        public Task InitializeAsync()
        {
            Store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;

                opts.GeneratedCodeMode = TypeLoadMode.Auto;
                opts.ApplicationAssembly = GetType().Assembly;
            });

            // Do this exactly once and no more.
            return Store.Advanced.Clean.CompletelyRemoveAllAsync();
        }

        public DocumentStore Store { get; private set; }

        public Task DisposeAsync()
        {
            Store.Dispose();
            return Task.CompletedTask;
        }
    }
}
