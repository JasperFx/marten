using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.DatabaseMultiTenancy
{
    [Collection("multi-tenancy")]
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
        public void default_tenant_usage_is_disabled()
        {
            theStore.Options.Advanced
                .DefaultTenantUsageEnabled.ShouldBeFalse();
        }

        [Fact]
        public async Task creates_databases_from_apply()
        {
            using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();

            (await conn.DatabaseExists("database1")).ShouldBeTrue();
            (await conn.DatabaseExists("tenant3")).ShouldBeTrue();
            (await conn.DatabaseExists("tenant4")).ShouldBeTrue();

        }

        [Fact]
        public async Task changes_are_applied_to_each_database()
        {
            var store = _host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
            var databases = await store.Tenancy.BuildDatabases();

            foreach (IMartenDatabase database in databases)
            {
                using var conn = database.CreateConnection();
                await conn.OpenAsync();

                var tables = await conn.ExistingTables();
                tables.Any(x => x.QualifiedName == "public.mt_doc_user").ShouldBeTrue();
                tables.Any(x => x.QualifiedName == "public.mt_doc_target").ShouldBeTrue();
            }
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

        [Fact]
        public async Task can_use_bulk_inserts()
        {
            var targets3 = Target.GenerateRandomData(100).ToArray();
            var targets4 = Target.GenerateRandomData(50).ToArray();

            await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

            await theStore.BulkInsertDocumentsAsync("tenant3", targets3);
            await theStore.BulkInsertDocumentsAsync("tenant4", targets4);

            using (var query3 = theStore.QuerySession("tenant3"))
            {
                var ids = await query3.Query<Target>().Select(x => x.Id).ToListAsync();

                ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets3.OrderBy(x => x.Id).Select(x => x.Id).ToList());
            }

            using (var query4 = theStore.QuerySession("tenant4"))
            {
                var ids = await query4.Query<Target>().Select(x => x.Id).ToListAsync();

                ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets4.OrderBy(x => x.Id).Select(x => x.Id).ToList());
            }
        }

        [Fact]
        public async Task clean_crosses_the_tenanted_databases()
        {
            var targets3 = Target.GenerateRandomData(100).ToArray();
            var targets4 = Target.GenerateRandomData(50).ToArray();

            await theStore.BulkInsertDocumentsAsync("tenant3", targets3);
            await theStore.BulkInsertDocumentsAsync("tenant4", targets4);

            await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

            using (var query3 = theStore.QuerySession("tenant3"))
            {
                (await query3.Query<Target>().AnyAsync()).ShouldBeFalse();
            }

            using (var query4 = theStore.QuerySession("tenant4"))
            {
                (await query4.Query<Target>().AnyAsync()).ShouldBeFalse();
            }
        }
    }
}
