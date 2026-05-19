using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.MultiTenancy;
using Lamar;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests;

public class reading_configuration_from_jasperfxoptions
{
    [Fact]
    public void all_the_defaults()
    {
        using var container = Container.For(services =>
        {
            services.AddMarten(ConnectionSource.ConnectionString);
        });


        var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

        store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
    }

    [Fact]
    public void can_override_tenancy_id_style()
    {
        using var container = Container.For(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
            });

            services.CritterStackDefaults(x => x.TenantIdStyle = TenantIdStyle.ForceUpperCase);
        });

        container.GetInstance<IDocumentStore>().As<DocumentStore>().Options.TenantIdStyle.ShouldBe(TenantIdStyle.ForceLowerCase);
    }

    [Fact]
    public void use_default_tenancy_id_style()
    {
        using var container = Container.For(services =>
        {
            services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
            });

            services.CritterStackDefaults(x => x.TenantIdStyle = TenantIdStyle.ForceUpperCase);
        });

        container.GetInstance<IDocumentStore>().As<DocumentStore>().Options.TenantIdStyle.ShouldBe(TenantIdStyle.ForceUpperCase);
    }
}
