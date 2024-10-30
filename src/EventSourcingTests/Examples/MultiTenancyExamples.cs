using System.Threading.Tasks;
using JasperFx;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weasel.Core;

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
                        options.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                        {
                            x.ConnectionString = masterConnection;

                            // You can optionally configure the schema name for where the mt_tenants
                            // table is stored
                            x.SchemaName = "tenants";

                            // If set, this will override the database schema rules for
                            // only the master tenant table from the parent StoreOptions
                            x.AutoCreate = AutoCreate.CreateOrUpdate;

                            // Optionally seed rows in the master table. This may be very helpful for
                            // testing or local development scenarios
                            // This operation is an "upsert" upon application startup
                            x.RegisterDatabase("tenant1", configuration.GetConnectionString("tenant1"));
                            x.RegisterDatabase("tenant2", configuration.GetConnectionString("tenant2"));
                            x.RegisterDatabase("tenant3", configuration.GetConnectionString("tenant3"));

                            // Tags the application name to all the used connection strings as a diagnostic
                            // Default is the name of the entry assembly for the application or "Marten" if
                            // .NET cannot determine the entry assembly for some reason
                            x.ApplicationName = "MyApplication";
                        });

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
