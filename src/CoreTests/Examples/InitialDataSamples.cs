using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreTests.Examples;

internal static class Program
{
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        throw new NotImplementedException();
    }
}

public class InitialDataSamples
{
    public static async Task use_testing_data()
    {
        #region sample_using_InitializeMartenWith

        // Use the configured host builder for your application
        // by calling the Program.CreateHostBuilder() method from
        // your application

        // This would be slightly different using WebApplicationFactory,
        // but the IServiceCollection mechanisms would be the same
        var hostBuilder = Program.CreateHostBuilder(Array.Empty<string>());

        // Add initial data to the application's Marten store
        // in the test project
        using var host = await hostBuilder
            .ConfigureServices(services =>
            {
                services.InitializeMartenWith<MyTestingData>();

                // or

                services.InitializeMartenWith(new MyTestingData());
            }).StartAsync();

        // The MyTestingData initial data set would be applied at
        // this point
        var store = host.Services.GetRequiredService<IDocumentStore>();

        // And in between tests, maybe do this to wipe out the store, then reapply
        // MyTestingData:
        await store.Advanced.ResetAllData();

        #endregion
    }
}

#region sample_MyTestingData

public class MyTestingData: IInitialData
{
    public Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        // TODO -- add baseline test data here
        return Task.CompletedTask;
    }
}

#endregion