using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.DatabaseMultiTenancy
{
    public class using_per_database_multitenancy : IAsyncLifetime
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

                    }).ApplyAllDatabaseChangesOnStartup();
                }).StartAsync();

            theStore = _host.Services.GetRequiredService<IDocumentStore>();
        }

        public Task DisposeAsync()
        {
            return _host.StopAsync();
        }

        [Fact]
        public async Task can_open_a_session_to_a_different_database()
        {
            var session =
                await theStore.OpenSessionAsync(new SessionOptions
                {
                    TenantId = "tenant1", Tracking = DocumentTracking.None
                });

            session.Connection.Database.ShouldBe("database1");
        }
    }
}
