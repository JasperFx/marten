using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace ContainerScopedProjectionTests;

public class configuration_specs
{
    [Fact]
    public async Task get_the_default_projection_name_for_scoped()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";


                }).AddProjectionWithServices<LetterProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped);
            }).StartAsync();

        var source = host.DocumentStore().Options.As<StoreOptions>().Projections.All.Single();
        source.Name.ShouldBe(nameof(LetterProjection));
    }

    [Fact]
    public async Task get_the_default_projection_name_for_singleton()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";


                }).AddProjectionWithServices<LetterProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Singleton);
            }).StartAsync();

        var source = host.DocumentStore().Options.As<StoreOptions>().Projections.All.Single();
        source.Name.ShouldBe(nameof(LetterProjection));
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task honor_projection_version_attribute_on_iprojection(ServiceLifetime lifetime)
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";


                }).AddProjectionWithServices<LetterProjectionV3>(ProjectionLifecycle.Inline, lifetime);
            }).StartAsync();

        var source = host.DocumentStore().Options.As<StoreOptions>().Projections.All.Single();
        source.Version.ShouldBe(3U);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task honor_projection_version_attribute_on_eventprojection(ServiceLifetime lifetime)
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";


                }).AddProjectionWithServices<LetterProjection2V3>(ProjectionLifecycle.Inline, lifetime);
            }).StartAsync();

        var source = host.DocumentStore().Options.As<StoreOptions>().Projections.All.Single();
        source.Version.ShouldBe(3U);
    }
}
