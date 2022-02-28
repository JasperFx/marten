using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing
{
    public class multi_tenancy_by_database : IAsyncLifetime
    {
        private IHost _host;
        private IDocumentStore theStore;

        public async Task InitializeAsync()
        {
            _host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(opts =>
                    {
                        opts
                            .MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                            .WithTenants("tenant1", "tenant2").InDatabaseNamed("database1")
                            .WithTenants("tenant3", "tenant4"); // own database


                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                    }).ApplyAllDatabaseChangesOnStartup();
                }).StartAsync();

            theStore = _host.Services.GetRequiredService<IDocumentStore>();
        }

        public Task DisposeAsync()
        {
            return _host.StopAsync();
        }

        [Fact]
        public async Task fail_when_trying_to_create_daemon_with_no_tenant()
        {
            await Should.ThrowAsync<DefaultTenantUsageDisabledException>(async () =>
            {
                await theStore.BuildProjectionDaemonAsync();
            });
        }

        [Fact]
        public async Task fail_when_trying_to_create_daemon_with_no_tenant_sync()
        {
            Should.Throw<DefaultTenantUsageDisabledException>(() =>
            {
                theStore.BuildProjectionDaemon();
            });
        }

        [Fact]
        public async Task build_daemon_for_database()
        {
            using var daemon = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync("tenant1");

            daemon.Database.Identifier.ShouldBe("database1");

            using var conn = daemon.Database.CreateConnection();
            conn.Database.ShouldBe("database1");
        }


        [Fact]
        public void build_daemon_for_database_sync()
        {
            using var daemon = (ProjectionDaemon)theStore.BuildProjectionDaemon("tenant1");

            daemon.Database.Identifier.ShouldBe("database1");

            using var conn = daemon.Database.CreateConnection();
            conn.Database.ShouldBe("database1");
        }
    }
}
