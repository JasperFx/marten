using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// Regression test for #4187: document tables from ancillary stores should never
/// be created in the main store's database.
/// </summary>
public class Bug_4187_ancillary_store_isolation
{
    // Types only used in the primary store
    public class PrimaryDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    // Types only used in the ancillary store
    public class AncillaryDoc
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = "";
    }

    // The ancillary store interface
    public interface IAncillaryStore : IDocumentStore;

    [Fact]
    public async Task ancillary_store_types_should_not_appear_in_primary_store_schema()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "primary_4187";
                    opts.RegisterDocumentType<PrimaryDoc>();
                    // Explicitly do NOT register AncillaryDoc
                })
                .ApplyAllDatabaseChangesOnStartup();

                services.AddMartenStore<IAncillaryStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ancillary_4187";
                    opts.RegisterDocumentType<AncillaryDoc>();
                    // Explicitly do NOT register PrimaryDoc
                });
            })
            .StartAsync();

        var primaryStore = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
        var ancillaryStore = (DocumentStore)host.Services.GetRequiredService<IAncillaryStore>();

        // Verify: primary store should only know about PrimaryDoc
        var primaryMappings = primaryStore.StorageFeatures
            .AllDocumentMappings
            .Select(m => m.DocumentType)
            .ToList();

        primaryMappings.ShouldContain(typeof(PrimaryDoc));
        primaryMappings.ShouldNotContain(typeof(AncillaryDoc),
            "AncillaryDoc should NOT be registered in the primary store");

        // Verify: ancillary store should only know about AncillaryDoc
        var ancillaryMappings = ancillaryStore.StorageFeatures
            .AllDocumentMappings
            .Select(m => m.DocumentType)
            .ToList();

        ancillaryMappings.ShouldContain(typeof(AncillaryDoc));
        ancillaryMappings.ShouldNotContain(typeof(PrimaryDoc),
            "PrimaryDoc should NOT be registered in the ancillary store");
    }

    [Fact]
    public async Task ancillary_store_ddl_should_not_contain_primary_store_types()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "primary_4187b";
                    opts.RegisterDocumentType<PrimaryDoc>();
                })
                .ApplyAllDatabaseChangesOnStartup();

                services.AddMartenStore<IAncillaryStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ancillary_4187b";
                    opts.RegisterDocumentType<AncillaryDoc>();
                });
            })
            .StartAsync();

        var primaryStore = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
        var ancillaryStore = (DocumentStore)host.Services.GetRequiredService<IAncillaryStore>();

        // Check the schema object names (tables, functions) that each store would create
        var primaryObjects = primaryStore.Storage.Database
            .AllObjects()
            .Select(o => o.Identifier.QualifiedName)
            .ToList();

        var ancillaryObjects = ancillaryStore.Storage.Database
            .AllObjects()
            .Select(o => o.Identifier.QualifiedName)
            .ToList();

        primaryObjects.Any(n => n.Contains("primarydoc")).ShouldBeTrue(
            "Primary store should have PrimaryDoc schema objects");
        primaryObjects.Any(n => n.Contains("ancillarydoc")).ShouldBeFalse(
            "Primary store should NOT have AncillaryDoc schema objects");

        ancillaryObjects.Any(n => n.Contains("ancillarydoc")).ShouldBeTrue(
            "Ancillary store should have AncillaryDoc schema objects");
        ancillaryObjects.Any(n => n.Contains("primarydoc")).ShouldBeFalse(
            "Ancillary store should NOT have PrimaryDoc schema objects");
    }
}
