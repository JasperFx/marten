using System.Threading.Tasks;
using Marten.CommandLine.Commands;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Marten.CommandLine.Testing
{
    public class ApplyThenAssertSmokeTests
    {
        [Fact]
        public async Task do_it_all()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Schema.For<User>();
            options.Schema.For<Target>();

            await new DocumentStore(options).Advanced.Clean.CompletelyRemoveAllAsync();

            await new ApplyCommand().Execute(new MartenInput
            {
                HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options))
            });

            await new AssertCommand().Execute(new MartenInput
            {
                HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options))
            });
        }
    }
}
