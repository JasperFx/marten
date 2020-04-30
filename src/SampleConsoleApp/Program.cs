using Marten;
using Marten.CommandLine;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace SampleConsoleApp
{
    // SAMPLE: SampleConsoleApp
    public class Program
    {
        public static int Main(string[] args)
        {
            var options = configureStoreOptions();

            // MartenCommands is from the Marten.CommandLine nuget
            return MartenCommands.Execute(options, args);
        }

        // build out the StoreOptions that you need for your application
        private static StoreOptions configureStoreOptions()
        {
            var options = new StoreOptions();

            options.Schema.For<User>();
            options.Schema.For<Issue>();
            options.Schema.For<Target>();

            options.Connection(ConnectionSource.ConnectionString);

            return options;
        }
    }

    // ENDSAMPLE
}
