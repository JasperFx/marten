using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.RuntimeCompiler;
using Marten;
using Marten.Events.Aggregation;
using Marten.Internal;
using Marten.Schema;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public interface IBug4185Store : IDocumentStore { }
public interface IBug4185OtherStore : IDocumentStore { }

public record OrderPlaced4185(string ProductName, decimal UnitPrice, int Quantity);
public record OrderShipped4185(DateTime ShippedAt);

public class OrderSummary4185
{
    public Guid Id { get; set; }
    public string ProductName { get; set; }
    public decimal Total { get; set; }
    public bool Shipped { get; set; }
}

public class RefProduct4185
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}

/// <summary>
/// Projection on the primary store that directly injects a secondary store.
/// This is the pattern that causes the codegen conflict.
/// </summary>
public class OrderProjection4185 : SingleStreamProjection<OrderSummary4185, Guid>
{
    private readonly IBug4185Store _secondaryStore;

    public OrderProjection4185(IBug4185Store secondaryStore)
    {
        _secondaryStore = secondaryStore;
    }

    public OrderSummary4185 Create(OrderPlaced4185 e)
    {
        return new OrderSummary4185
        {
            ProductName = e.ProductName,
            Total = e.UnitPrice * e.Quantity
        };
    }

    public void Apply(OrderShipped4185 e, OrderSummary4185 summary)
    {
        summary.Shipped = true;
    }
}

/// <summary>
/// Reproduces https://github.com/JasperFx/marten/issues/4185
///
/// When a projection registered on the primary store has a direct dependency
/// on a secondary document store, the SecondaryDocumentStores ICodeFileCollection
/// and the SecondaryStoreConfig.Build() method use DIFFERENT output paths for
/// writing the generated store implementation class:
///
///   - SecondaryDocumentStores.Rules → output path = {base}/ + ChildNamespace "Stores"
///     → writes to {base}/Stores/{StoreImpl}.cs
///
///   - SecondaryStoreConfig.Build() → uses rules from options.CreateGenerationRules()
///     which includes StoreName in the path → {base}/{StoreName}/ + ChildNamespace "Stores"
///     → writes to {base}/{StoreName}/Stores/{StoreImpl}.cs
///
/// Both files have the same namespace (Marten.Generated.Stores) and class name,
/// causing CS0101 duplicate type definition when using TypeLoadMode.Static
/// where generated code is compiled as part of the project build.
/// </summary>
public class Bug_4185_codegen_conflict_projection_with_secondary_store_dependency
{
    /// <summary>
    /// Verifies that resolving secondary stores does not write duplicate
    /// store implementation files to different output directories.
    /// </summary>
    [Fact]
    public async Task secondary_store_impl_should_not_be_generated_in_store_specific_subdirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bug4185_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IAssemblyGenerator, AssemblyGenerator>();

                    services.AddMartenStore<IBug4185Store>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4185_sec";
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;
                        opts.GeneratedCodeOutputPath = tempDir;
                    });

                    services.AddMartenStore<IBug4185OtherStore>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4185_oth";
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;
                        opts.GeneratedCodeOutputPath = tempDir;
                    });

                    services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4185_pri";
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;
                        opts.GeneratedCodeOutputPath = tempDir;
                    });
                })
                .StartAsync();

            // Resolve the secondary stores to trigger code generation
            var store1 = host.Services.GetRequiredService<IBug4185Store>();
            var store2 = host.Services.GetRequiredService<IBug4185OtherStore>();

            store1.ShouldNotBeNull();
            store2.ShouldNotBeNull();

            // Check that store implementation files are NOT duplicated
            var allFiles = Directory.GetFiles(tempDir, "*.cs", SearchOption.AllDirectories);

            var bug4185StoreFiles = allFiles
                .Where(f => Path.GetFileName(f).Contains("IBug4185StoreImplementation"))
                .ToList();

            // The store impl should only exist in ONE location, not both
            // {tempDir}/Stores/ AND {tempDir}/IBug4185Store/Stores/
            bug4185StoreFiles.Count.ShouldBe(1,
                $"Expected 1 file for IBug4185StoreImplementation but found {bug4185StoreFiles.Count}:\n" +
                string.Join("\n", bug4185StoreFiles.Select(f => f.Replace(tempDir, ""))));

            var otherStoreFiles = allFiles
                .Where(f => Path.GetFileName(f).Contains("IBug4185OtherStoreImplementation"))
                .ToList();

            otherStoreFiles.Count.ShouldBe(1,
                $"Expected 1 file for IBug4185OtherStoreImplementation but found {otherStoreFiles.Count}:\n" +
                string.Join("\n", otherStoreFiles.Select(f => f.Replace(tempDir, ""))));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task projection_with_secondary_store_dependency_should_work_at_runtime()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IBug4185Store>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "bug4185_sec";
                    opts.GeneratedCodeMode = TypeLoadMode.Auto;
                });

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4185_pri";
                        opts.GeneratedCodeMode = TypeLoadMode.Auto;
                    })
                    .AddProjectionWithServices<OrderProjection4185>(
                        ProjectionLifecycle.Inline,
                        ServiceLifetime.Singleton)
                    .ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId, new OrderPlaced4185("Widget", 9.99m, 3));
            await session.SaveChangesAsync();
        }

        await using (var session = store.QuerySession())
        {
            var summary = await session.LoadAsync<OrderSummary4185>(streamId);
            summary.ShouldNotBeNull();
            summary.ProductName.ShouldBe("Widget");
            summary.Total.ShouldBe(29.97m);
        }
    }
}
