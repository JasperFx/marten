using Daemon.StressTest;
using JasperFx.CodeGeneration;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
    .MinimumLevel.Override("Npgsql.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Async(a => a.Console());

builder.Services.AddSerilog(logger.CreateLogger());

builder.Services.AddMarten(c =>
{
    c.CreateDatabasesForTenants(t => t
        .ForTenant()
        .CheckAgainstPgDatabase());

    c.Connection("");
    c.UseNewtonsoftForSerialization();

    c.Projections.Add<StressedEventProjection>(ProjectionLifecycle.Async);

    c.Policies.AllDocumentsAreMultiTenanted();

    c.GeneratedCodeMode = TypeLoadMode.Auto;
    c.SourceCodeWritingEnabled = true;

}).AddAsyncDaemon(DaemonMode.Solo)
.ApplyAllDatabaseChangesOnStartup();


builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
