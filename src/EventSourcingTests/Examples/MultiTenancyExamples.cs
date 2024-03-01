using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class MultiTenancyExamples
{
    public static async Task using_master_table_multi_tenancy()
    {
        #region sample_master_table_multi_tenancy

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(sp =>
                    {
                        var configuration = sp.GetRequiredService<IConfiguration>();
                        var masterConnection = configuration.GetConnectionString("master");
                        var options = new StoreOptions();

                        // This is opting into a multi-tenancy model where a database table in the
                        // master database holds information about all the possible tenants and their database connection
                        // strings
                        options.MultiTenantedDatabasesWithMasterDatabaseTable(masterConnection, "tenants");

                        // Other Marten configuration

                        return options;
                    })
                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();;
            }).StartAsync();

        #endregion
    }
}
