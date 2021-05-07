using System.Threading.Tasks;
using Marten;
using Marten.AsyncDaemon.Testing;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Oakton;
using Weasel.Postgresql;

namespace CommandLineRunner
{
    public class Program
    {
        public static Task<int> Main(string[] args)
        {
            return CreateHostBuilder(args).RunOaktonCommands(args);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddMarten(opts =>
                    {
                        opts.AutoCreateSchemaObjects = AutoCreate.All;
                        opts.DatabaseSchemaName = "cli";
                        opts.Connection(ConnectionSource.ConnectionString);

                        opts.Events.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
                        opts.Events.Projections.Add(new DayProjection(), ProjectionLifecycle.Async);
                        opts.Events.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);
                    });
                });
        }
    }
}
