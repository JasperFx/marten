using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MultiTenancyTests;

public interface IBug6190Store: IDocumentStore;

/// <summary>
///     #6190: <c>IDynamicTenantSource&lt;string&gt;</c> was registered only by <c>AddMarten(StoreOptions)</c> (and the
///     <c>Action</c> overload through it), because only there is the tenancy known at registration. A store configured
///     with <c>AddMarten(Func&lt;IServiceProvider, StoreOptions&gt;)</c>, through <c>ConfigureMarten()</c>, or added with
///     <c>AddMartenStore&lt;T&gt;()</c> opts in with <c>ExposeDynamicTenantSource()</c>.
///     <para>
///         A lazy registration cannot be made for every store instead: the container keeps a null a factory returns
///         in <c>GetServices()</c>, so a store whose tenancy is not dynamic would hand consumers a null.
///     </para>
/// </summary>
public class dynamic_tenant_source_registration
{
    private const string MasterSchema = "bug_6190_master";

    private static void masterTable(StoreOptions opts)
    {
        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
        {
            x.ConnectionString = ConnectionSource.ConnectionString;
            x.SchemaName = MasterSchema;
        });
        opts.DatabaseSchemaName = "bug_6190";
    }

    [Fact]
    public async Task the_service_provider_form_exposes_its_dynamic_tenancy_when_asked()
    {
        var services = new ServiceCollection();
        services.AddMarten(_ =>
        {
            var opts = new StoreOptions();
            masterTable(opts);
            return opts;
        }).ExposeDynamicTenantSource();

        await using var provider = services.BuildServiceProvider();

        var source = provider.GetServices<IDynamicTenantSource<string>>().ShouldHaveSingleItem();
        source.ShouldBeSameAs(provider.GetRequiredService<IDocumentStore>().As<DocumentStore>().Tenancy);
        source.ShouldBeOfType<MasterTableTenancy>();
    }

    [Fact]
    public async Task tenancy_set_through_configure_marten_is_exposed_when_asked()
    {
        var services = new ServiceCollection();
        services.AddMarten(ConnectionSource.ConnectionString).ExposeDynamicTenantSource();
        services.ConfigureMarten(masterTable);

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IDynamicTenantSource<string>>().ShouldHaveSingleItem()
            .ShouldBeOfType<MasterTableTenancy>();
    }

    [Fact]
    public async Task an_ancillary_store_exposes_its_dynamic_tenancy_when_asked()
    {
        var services = new ServiceCollection();
        services.AddMartenStore<IBug6190Store>(masterTable).ExposeDynamicTenantSource();

        await using var provider = services.BuildServiceProvider();

        var source = provider.GetServices<IDynamicTenantSource<string>>().ShouldHaveSingleItem();
        source.ShouldBeSameAs(provider.GetRequiredService<IBug6190Store>().As<DocumentStore>().Tenancy);
    }

    [Fact]
    public async Task asking_where_the_store_already_exposes_it_registers_it_once()
    {
        var services = new ServiceCollection();
        services.AddMarten(masterTable).ExposeDynamicTenantSource();

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IDynamicTenantSource<string>>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task a_store_that_does_not_ask_exposes_nothing()
    {
        var services = new ServiceCollection();
        services.AddMarten(_ => new StoreOptions { DatabaseSchemaName = "bug_6190" }
            .Tap(x => x.Connection(ConnectionSource.ConnectionString)));

        await using var provider = services.BuildServiceProvider();

        provider.GetServices<IDynamicTenantSource<string>>().ShouldBeEmpty();
    }

    [Fact]
    public async Task asking_for_a_store_whose_tenancy_is_not_dynamic_says_so()
    {
        var services = new ServiceCollection();
        services.AddMarten(ConnectionSource.ConnectionString).ExposeDynamicTenantSource();

        await using var provider = services.BuildServiceProvider();

        var ex = Should.Throw<InvalidOperationException>(() =>
            provider.GetServices<IDynamicTenantSource<string>>().ToList());
        ex.Message.ShouldContain("ExposeDynamicTenantSource()");
    }
}

internal static class StoreOptionsTapExtensions
{
    public static StoreOptions Tap(this StoreOptions options, Action<StoreOptions> configure)
    {
        configure(options);
        return options;
    }
}
