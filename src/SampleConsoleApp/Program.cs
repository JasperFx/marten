using Marten;
using Marten.CommandLine;
using Marten.Testing;
using Marten.Testing.Documents;

namespace SampleConsoleApp
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var options = new StoreOptions();

            options.Schema.For<User>();
            options.Schema.For<Issue>();
            options.Schema.For<Target>();


            options.Connection(ConnectionSource.ConnectionString);

            return MartenCommands.Execute(options, args);
        }
    }
}