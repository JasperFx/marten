using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Transforms
{
    public class document_transforms_multi_tenancy : IAsyncLifetime
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
                        opts.UseJavascriptTransformsAndPatching(x => x.LoadFile("default_username.js"));

                        opts
                            .MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                            .WithTenants("tenant3", "tenant4"); // own database


                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                    }).ApplyAllDatabaseChangesOnStartup();
                }).StartAsync();

            theStore = _host.Services.GetRequiredService<IDocumentStore>();

            await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        }

        public Task DisposeAsync()
        {
            return _host.StopAsync();
        }

        [Fact]
        public async Task transform_for_tenants()
        {

            var user1 = new MultiTenantUser() { FirstName = "Jeremy", LastName = "Miller" };
            var user2 = new MultiTenantUser { FirstName = "Corey", LastName = "Kaylor" };
            var user3 = new MultiTenantUser { FirstName = "Tim", LastName = "Cools", UserName = "NotTransformed" };

            await theStore.BulkInsertAsync("Purple", new MultiTenantUser[] { user1, user2 });
            await theStore.BulkInsertAsync("Orange", new MultiTenantUser[] { user3 });

            await theStore.TransformAsync("Purple",x => x.All<MultiTenantUser>("default_username"));

            using (var query = theStore.QuerySession("Purple"))
            {
                query.Load<MultiTenantUser>(user1.Id).UserName.ShouldBe("jeremy.miller");
            }

            using (var query = theStore.QuerySession("Orange"))
            {
                query.Load<MultiTenantUser>(user3.Id).UserName.ShouldBe("NotTransformed");
            }
        }
    }
}
