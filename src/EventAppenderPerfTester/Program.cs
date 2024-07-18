using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Oakton;

var builder = Host.CreateDefaultBuilder();
builder.ConfigureServices(services =>
{
    services.AddMarten(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);

        opts.DisableNpgsqlLogging = true;

        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseIdentityMapForInlineAggregates = true;

        opts.Projections.Add<DaemonTests.TestingSupport.TripProjection>(ProjectionLifecycle.Inline);
    });
});

return await builder.RunOaktonCommands(args);

