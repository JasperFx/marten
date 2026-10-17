using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DaemonTests.Specifications;

/// <summary>
/// An application whose only async projection is <see cref="ShipmentSummaryProjection" />, hosted once per test
/// class the way an application's own spec project would host it. <see cref="RunsTheDaemon" /> false hosts the
/// same store with the daemon disabled, which is how a catch-up timeout is forced on purpose.
/// </summary>
public abstract class ShipmentsHostBase: IAsyncLifetime
{
    private readonly string _schemaName;
    private readonly bool _runsTheDaemon;

    protected ShipmentsHostBase(string schemaName, bool runsTheDaemon)
    {
        _schemaName = schemaName;
        _runsTheDaemon = runsTheDaemon;
    }

    public IHost Host { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var marten = services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = _schemaName;
                    opts.Projections.Add<ShipmentSummaryProjection>(ProjectionLifecycle.Async);
                });

                if (_runsTheDaemon) marten.AddAsyncDaemon(DaemonMode.Solo);
            })
            .Build();

        // Clean BEFORE the daemon starts. Cleaning a running daemon's store restarts the event sequence underneath
        // shards that have already loaded their old progress, so they sit "ahead" of every new event and never run
        // it: every read then times out with 'ShipmentSummary:All' at 0 of N. Whether the clean won that race
        // depended on timing, which is why it passed alone and failed in a full run.
        var store = Host.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Advanced.Clean.DeleteAllEventDataAsync(TestContext.Current.CancellationToken);

        await Host.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is null) return;

        await Host.StopAsync();
        Host.Dispose();
    }
}

public sealed class ShipmentsHost(): ShipmentsHostBase("daemon_specs", runsTheDaemon: true);

public sealed class ShipmentsHostWithNoDaemon(): ShipmentsHostBase("daemon_specs_no_daemon", runsTheDaemon: false);
