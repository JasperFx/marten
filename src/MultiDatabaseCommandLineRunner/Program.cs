using JasperFx.CodeGeneration;
using Marten;
using DaemonTests;
using DaemonTests.TestingSupport;
using JasperFx;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;

public interface IOtherStore: IDocumentStore
{
}

public class Program
{
    public static Task<int> Main(string[] args)
    {
        return CreateHostBuilder(args).RunJasperFxCommands(args);
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddMartenStore<IOtherStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.RegisterDocumentType<Target>();
                    opts.GeneratedCodeMode = TypeLoadMode.Auto;
                });

                services.AddMarten(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                    opts.DatabaseSchemaName = "cli";

                    opts.MultiTenantedWithSingleServer(
                        ConnectionSource.ConnectionString,
                        t => t.WithTenants("chiefs", "chargers", "broncos", "raiders")
                    );

                    // This is important, setting this option tells Marten to
                    // *try* to use pre-generated code at runtime
                    opts.GeneratedCodeMode = TypeLoadMode.Auto;

                    // You have to register all persisted document types ahead of time
                    // RegisterDocumentType<T>() is the equivalent of saying Schema.For<T>()
                    // just to let Marten know that document type exists
                    opts.RegisterDocumentType<Target>();
                    opts.RegisterDocumentType<User>();

                    // Register all event store projections ahead of time
                    opts.Projections
                        .Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);

                    opts.Projections
                        .Add(new DayProjection(), ProjectionLifecycle.Async);

                    opts.Projections
                        .Add(new DistanceProjection(), ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            });
    }
}
