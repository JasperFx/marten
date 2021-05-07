using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Baseline;
using Marten.CommandLine.Commands.Patch;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Marten.CommandLine.Testing
{
    public class PatchCommandSmokeTests
    {

        [Fact]
        public async Task can_write_both_files()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Schema.For<User>();
            options.Schema.For<Target>();

            var input = new PatchInput()
            {
                HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options)),
                FileName = Path.GetTempPath().AppendPath("dump1.sql"),


            };

            await new PatchCommand().Execute(input);
        }
    }
}
