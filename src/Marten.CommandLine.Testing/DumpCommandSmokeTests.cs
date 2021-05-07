using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.CommandLine.Commands.Dump;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Marten.CommandLine.Testing
{
    public class DumpCommandSmokeTests
    {
        [Fact]
        public async Task can_clean_repeatedly_for_directory()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Schema.For<User>();
            options.Schema.For<Target>();

            var input = new DumpInput
            {
                HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options)),
                FileName = Path.GetTempPath().AppendPath("dump1"),


            };

            await new DumpCommand().Execute(input);
            Thread.Sleep(100); // Let the file system calm down

            input.HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options));
            await new DumpCommand().Execute(input);
        }

        [Fact]
        public async Task can_clean_repeatedly_for_file()
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Schema.For<User>();
            options.Schema.For<Target>();



            var input = new DumpInput
            {
                HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options)),
                FileName = Path.GetTempPath().AppendPath("dump2", "file.sql"),


            };

            await new DumpCommand().Execute(input);
            Thread.Sleep(100); // Let the file system calm down

            input.HostBuilder = new HostBuilder().ConfigureServices(x => x.AddMarten(options));
            await new DumpCommand().Execute(input);
        }
    }
}
