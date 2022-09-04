
using System.Diagnostics;
using LamarCodeGeneration;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Oakton;
using Weasel.Core;

namespace EventStoreMigrations;

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

public class Started{}