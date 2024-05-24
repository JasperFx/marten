using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CoreTests;

public class configuring_marten_with_async_extensions
{
    [Fact]
    public async Task feature_flag_positive()
    {
        var featureManager = Substitute.For<IFeatureManager>();
        featureManager.IsEnabledAsync("Module1").Returns(true);

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "async_config";
                }).ApplyAllDatabaseChangesOnStartup();

                #region sample_registering_async_config_marten

                services.ConfigureMartenWithServices<FeatureManagementUsingExtension>();

                #endregion
                services.AddSingleton(featureManager);
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();

        store.Events.EventMappingFor<Module1Event>()
            .Alias.ShouldBe("module1:event");

    }

    [Fact]
    public async Task feature_flag_negative()
    {
        var featureManager = Substitute.For<IFeatureManager>();

        featureManager.IsEnabledAsync("Module1").Returns(false);

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "async_config";
                });

                services.ConfigureMartenWithServices<FeatureManagementUsingExtension>();
                services.AddSingleton(featureManager);
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();

        store.Events.EventMappingFor<Module1Event>()
            .Alias.ShouldBe("module_1_event");

    }
}

#region sample_FeatureManagementUsingExtension

public class FeatureManagementUsingExtension: IAsyncConfigureMarten
{
    private readonly IFeatureManager _manager;

    public FeatureManagementUsingExtension(IFeatureManager manager)
    {
        _manager = manager;
    }

    public async ValueTask Configure(StoreOptions options, CancellationToken cancellationToken)
    {
        if (await _manager.IsEnabledAsync("Module1"))
        {
            options.Events.MapEventType<Module1Event>("module1:event");
        }
    }
}

#endregion

public class Module1Event
{

}

