using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;

#region sample_disabling_npgsql_logging

var builder = Host.CreateDefaultBuilder();
builder.ConfigureServices(services =>
{
    services.AddMarten(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);

        // Disable the absurdly verbose Npgsql logging
        opts.DisableNpgsqlLogging = true;

        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseIdentityMapForInlineAggregates = true;

        opts.Projections.Add<DaemonTests.TestingSupport.TripProjection>(ProjectionLifecycle.Inline);
    });
});

#endregion

return await builder.RunJasperFxCommands(args);

