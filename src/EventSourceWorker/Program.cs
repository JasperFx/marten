using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventSourceWorker;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                var configuration = hostContext.Configuration;
                var environment = hostContext.HostingEnvironment;

                services.AddHostedService<Worker>();

                // This is the absolute, simplest way to integrate Marten into your
                // .Net Core application with Marten's default configuration
                services.AddMarten(options =>
                    {
                        // Establish the connection string to your Marten database
                        options.Connection(configuration.GetConnectionString("Marten"));

                        // If we're running in development mode, let Marten just take care
                        // of all necessary schema building and patching behind the scenes
                        if (environment.IsDevelopment())
                        {
                            options.AutoCreateSchemaObjects = AutoCreate.All;
                        }

                        options.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Inline);
                    })
                    // Run the asynchronous projections in this node
                    .AddAsyncDaemon(DaemonMode.Solo);
            });
    }
}
