using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core.CommandLine;
using Xunit;

namespace CoreTests;

public class jasper_fx_mechanics
{
    /* TODOs
     * Test integration w/ AddJasperFx
     * Use defaults from JasperFxOptions for codegen & auto create
     * use overrides for codegen & auto create


     */

    [Fact]
    public async Task build_system_part_for_single_document_store_and_single_tenancy()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "system_part";
                });
            }).StartAsync();

        var part = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart>()
            .SingleOrDefault();

        part.ShouldNotBeNull();
        part.SubjectUri.ShouldBe(new Uri("marten://documentstore"));
        part.Title.ShouldBe("Marten");

        var resources = await part.FindResources();
        resources.Single().ShouldBeOfType<DatabaseResource>();

        host.Services.GetServices<IEventStore>().Single().ShouldBeOfType<DocumentStore>();

        var usage = await host.Services.GetRequiredService<IEventStore>().TryCreateUsage(CancellationToken.None);
        usage.SubjectUri.ShouldBe(new Uri("marten://main"));
    }

    [Fact]
    public async Task build_system_parts_for_ancillary_document_stores_and_single_tenancy()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });

                services.AddMartenStore<ISecondStore>(services =>
                {
                    var opts = new StoreOptions();
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "second_store";

                    return opts;
                });
            }).StartAsync();

        var part1 = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart<IFirstStore>>()
            .SingleOrDefault();

        part1.ShouldNotBeNull();
        part1.SubjectUri.ShouldBe(new Uri("marten://ifirststore"));
        part1.Title.ShouldBe("Marten IFirstStore");

        var part2 = host.Services
            .GetServices<ISystemPart>()
            .OfType<MartenSystemPart<ISecondStore>>()
            .SingleOrDefault();

        part2.ShouldNotBeNull();
        part2.SubjectUri.ShouldBe(new Uri("marten://isecondstore"));
        part2.Title.ShouldBe("Marten ISecondStore");

        host.Services.GetServices<IEventStore>().OfType<IFirstStore>().Any().ShouldBeTrue();
        host.Services.GetServices<IEventStore>().OfType<ISecondStore>().Any().ShouldBeTrue();

        var usage1 = await host.Services.GetServices<IEventStore>().Single(x => x is IFirstStore)
            .TryCreateUsage(CancellationToken.None);

        usage1.SubjectUri.ShouldBe(new Uri("marten://ifirststore"));

        var usage2 = await host.Services.GetServices<IEventStore>().Single(x => x is ISecondStore)
            .TryCreateUsage(CancellationToken.None);

        usage2.SubjectUri.ShouldBe(new Uri("marten://isecondstore"));
    }
}

public interface IFirstStore : IDocumentStore{}
public interface ISecondStore : IDocumentStore{}
