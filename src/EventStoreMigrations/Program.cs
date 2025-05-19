using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;

namespace EventStoreMigrations;

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
                services.AddMarten(opts =>
                {
                    opts.AutoCreateSchemaObjects = AutoCreate.None;
                    opts.DatabaseSchemaName = "cli";
                    opts.Connection(ConnectionSource.ConnectionString);

                    opts.GeneratedCodeMode = TypeLoadMode.Dynamic;

                    opts.Events.StreamIdentity = StreamIdentity.AsString;
                    opts.Events.MetadataConfig.HeadersEnabled = true;
                    opts.Events.AddEventType(typeof(Started));
                });
            });
    }
}

public class Started
{
}
