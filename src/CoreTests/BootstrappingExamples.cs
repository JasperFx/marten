using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreTests;

public static class UserModule
{
    #region sample_AddUserModule

    public static IServiceCollection AddUserModule(this IServiceCollection services)
    {
        // This applies additional configuration to the main Marten DocumentStore
        // that is configured elsewhere
        services.ConfigureMarten(opts =>
        {
            opts.RegisterDocumentType<User>();
        });

        // Other service registrations specific to the User submodule
        // within the bigger system

        return services;
    }

    #endregion
}

public static class UserModule2
{
    #region sample_AddUserModule2

    public static IServiceCollection AddUserModule2(this IServiceCollection services)
    {
        // This applies additional configuration to the main Marten DocumentStore
        // that is configured elsewhere
        services.AddSingleton<IConfigureMarten, UserMartenConfiguration>();

        // Other service registrations specific to the User submodule
        // within the bigger system

        return services;
    }

    #endregion
}

#region sample_UserMartenConfiguration

internal class UserMartenConfiguration: IConfigureMarten
{
    public void Configure(IServiceProvider services, StoreOptions options)
    {
        options.RegisterDocumentType<User>();
        // and any other additional Marten configuration
    }
}

#endregion

public class BootstrappingExamples
{


    public static async Task using_configure_marten()
    {
        #region sample_using_configure_marten

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // The initial Marten configuration
                services.AddMarten("some connection string");

                // Other core service registrations
                services.AddLogging();

                // Add the User module
                services.AddUserModule();
            }).StartAsync();

        #endregion

    }
}