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
                // Do this, or your environment check assertion failures below
                // is just swallowed and logged on startup
                services.Configure<HostOptions>(options =>
                {
                    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
                });

                services.AddMarten("connection string")
                    .AssertDatabaseMatchesConfigurationOnStartup();
            })
            .StartAsync();
    }

    #endregion
}
