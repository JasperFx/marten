using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests;

public interface ILazyTestStore : IDocumentStore;

public class lazy_ancillary_store_registration
{
    [Fact]
    public async Task lazy_of_ancillary_store_is_registered_as_singleton()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "lazy_test_primary";
                });

                services.AddMartenStore<ILazyTestStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "lazy_test_ancillary";
                });
            })
            .StartAsync();

        // Lazy<T> should be resolvable
        var lazy = host.Services.GetService<Lazy<ILazyTestStore>>();
        lazy.ShouldNotBeNull();

        // Should not be created yet
        lazy.IsValueCreated.ShouldBeFalse();

        // Resolving the value should return the same instance as direct resolution
        var fromLazy = lazy.Value;
        var direct = host.Services.GetRequiredService<ILazyTestStore>();

        fromLazy.ShouldBeSameAs(direct);
    }

    [Fact]
    public async Task lazy_of_ancillary_store_can_be_injected_into_a_service()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "lazy_test2_primary";
                });

                services.AddMartenStore<ILazyTestStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "lazy_test2_ancillary";
                });

                services.AddSingleton<ServiceThatUsesLazyStore>();
            })
            .StartAsync();

        var service = host.Services.GetRequiredService<ServiceThatUsesLazyStore>();
        service.ShouldNotBeNull();

        // The store should be accessible through the lazy wrapper
        var store = service.GetStore();
        store.ShouldNotBeNull();
        store.ShouldBeAssignableTo<ILazyTestStore>();
    }
}

public class ServiceThatUsesLazyStore
{
    private readonly Lazy<ILazyTestStore> _store;

    public ServiceThatUsesLazyStore(Lazy<ILazyTestStore> store)
    {
        _store = store;
    }

    public IDocumentStore GetStore() => _store.Value;
}
