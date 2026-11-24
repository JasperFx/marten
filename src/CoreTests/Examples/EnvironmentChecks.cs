using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreTests.Examples;

public class EnvironmentChecks
{
    #region sample_use_environment_check_in_hosted_service

    public static async Task use_environment_check()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // The assertion runs in the hosted service Marten registers
                // to activate the store, so a database that does not match
                // the configuration throws out of StartAsync() and the host
                // never starts
                services.AddMarten("connection string")
                    .AssertDatabaseMatchesConfigurationOnStartup();
            })
            .StartAsync();
    }

    #endregion
}
