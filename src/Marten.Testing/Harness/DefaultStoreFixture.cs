using System.Threading.Tasks;
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

                // TODO -- opt into auto code generation later.
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
