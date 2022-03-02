    using System.Diagnostics;
    using LamarCodeGeneration;
    using Marten;
    using Marten.Events.Daemon.Resiliency;
    using Marten.Events.Projections;
    using Marten.Testing.Documents;
    using Marten.Testing.Harness;
    using Microsoft.Extensions.Hosting;
    using Oakton;
    using Weasel.Core;

    public interface IOtherStore : IDocumentStore
    {

    }

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

                        opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                            .WithTenants("chiefs", "chargers", "broncos", "raiders");

                        // This is important, setting this option tells Marten to
                        // *try* to use pre-generated code at runtime
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;

                        // You have to register all persisted document types ahead of time
                        // RegisterDocumentType<T>() is the equivalent of saying Schema.For<T>()
                        // just to let Marten know that document type exists
                        opts.RegisterDocumentType<Target>();
                        opts.RegisterDocumentType<User>();


                    }).AddAsyncDaemon(DaemonMode.Solo);
                });
        }
    }

