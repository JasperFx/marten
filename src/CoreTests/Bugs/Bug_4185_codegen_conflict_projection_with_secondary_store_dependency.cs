using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Events.Projections;
using JasperFx.RuntimeCompiler;
using Marten;
using Marten.Events.Aggregation;
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
/// When a secondary document store is registered, the codegen write command
/// (DynamicCodeBuilder.WriteGeneratedCode) iterates all ICodeFileCollection
/// instances. The SecondaryDocumentStores collection writes the store
/// implementation to {base}/Stores/{StoreImpl}.cs.
///
/// Separately, at runtime, SecondaryStoreConfig.Build() calls
/// InitializeSynchronously() with rules from CreateGenerationRules() which
/// include the StoreName in the path, writing the same class to
/// {base}/{StoreName}/Stores/{StoreImpl}.cs.
///
/// Both files share namespace Marten.Generated.Stores and the same class name,
/// causing CS0101 duplicate type definition when using TypeLoadMode.Static.
/// </summary>
public class Bug_4185_codegen_conflict_projection_with_secondary_store_dependency
{
    /// <summary>
    /// Simulates what "dotnet run -- codegen write" does by using
    /// DynamicCodeBuilder.WriteGeneratedCode(), then verifies that
    /// no duplicate store implementation files are produced.
    /// </summary>
    [Fact]
    public async Task codegen_write_should_not_produce_duplicate_secondary_store_implementations()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "bug4185_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            using var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
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

            // Resolve the secondary stores — this triggers Build() which calls
            // InitializeSynchronously() and writes generated code to
            // {tempDir}/{StoreName}/Stores/ when SourceCodeWritingEnabled is true
            host.Services.GetRequiredService<IBug4185Store>();
            host.Services.GetRequiredService<IBug4185OtherStore>();

            // Now simulate "dotnet run -- codegen write" using DynamicCodeBuilder.
            // This writes generated code via each ICodeFileCollection's Rules,
            // including SecondaryDocumentStores which writes to {tempDir}/Stores/
            var collections = host.Services.GetServices<ICodeFileCollection>().ToArray();
            var codeBuilder = new DynamicCodeBuilder(host.Services, collections);
            codeBuilder.WriteGeneratedCode(_ => { });

            // Collect all generated .cs files
            var allFiles = Directory.GetFiles(tempDir, "*.cs", SearchOption.AllDirectories);

            // Check that each generated file's fully qualified type name is unique.
            // Read each file, extract the namespace + class name, and detect conflicts
            // where the same fully-qualified type is generated to multiple locations.
            var typeLocations = allFiles
                .Select(f => new
                {
                    Path = f.Replace(tempDir, ""),
                    Content = File.ReadAllText(f)
                })
                .Select(f =>
                {
                    var nsMatch = System.Text.RegularExpressions.Regex.Match(f.Content, @"namespace\s+([\w.]+)");
                    var classMatch = System.Text.RegularExpressions.Regex.Match(f.Content, @"class\s+(\w+)");
                    return new
                    {
                        f.Path,
                        FullyQualifiedName = nsMatch.Success && classMatch.Success
                            ? $"{nsMatch.Groups[1].Value}.{classMatch.Groups[1].Value}"
                            : null
                    };
                })
                .Where(f => f.FullyQualifiedName != null)
                .ToList();

            var duplicates = typeLocations
                .GroupBy(f => f.FullyQualifiedName)
                .Where(g => g.Count() > 1)
                .ToList();

            duplicates.ShouldBeEmpty(
                "codegen write produced duplicate types at different locations:\n" +
                string.Join("\n", duplicates.Select(g =>
                    $"  {g.Key}:\n" +
                    string.Join("\n", g.Select(f => "    " + f.Path)))));
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
