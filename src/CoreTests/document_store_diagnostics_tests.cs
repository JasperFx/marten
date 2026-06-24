using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Documents;
using JasperFx.Events;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class DiagWidget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public abstract class DiagAnimal
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class DiagDog: DiagAnimal { }

public class DiagCat: DiagAnimal { }

/// <summary>
/// Coverage for #4775: the store-agnostic <see cref="IDocumentStoreDiagnostics"/> query surface
/// (DI registration + DocumentTypesAsync/QueryDocumentsAsync/LoadDocumentJsonAsync) plus the
/// <see cref="DocumentMappingDescriptor"/> enrichment (SubClasses + structured Partitioning) that
/// feeds the CritterWatch Document Database Explorer.
/// </summary>
public class document_store_diagnostics_tests
{
    [Fact]
    public async Task diagnostics_is_registered_in_the_container()
    {
        using var host = await BuildHost("doc_diag_di", opts => opts.Schema.For<DiagWidget>());

        host.Services.GetService<IDocumentStoreDiagnostics>().ShouldNotBeNull();
    }

    [Fact]
    public async Task document_types_lists_registered_mappings()
    {
        using var host = await BuildHost("doc_diag_types", opts =>
        {
            opts.Schema.For<DiagWidget>();
            opts.Schema.For<User>();
        });

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
        var types = await diagnostics.DocumentTypesAsync(CancellationToken.None);

        var widget = types.Single(t => t.Alias == "diagwidget");
        widget.TypeName.ShouldContain(nameof(DiagWidget));
        widget.SchemaName.ShouldBe("doc_diag_types");

        types.Select(t => t.Alias).ShouldContain("user");
    }

    [Fact]
    public async Task query_documents_pages_and_reports_the_total()
    {
        using var host = await BuildHost("doc_diag_paging", opts => opts.Schema.For<DiagWidget>());

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            for (var i = 0; i < 5; i++)
            {
                session.Store(new DiagWidget { Id = Guid.NewGuid(), Name = $"widget-{i}" });
            }

            await session.SaveChangesAsync();
        }

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
        var typeName = typeof(DiagWidget).FullName!;

        var firstPage = await diagnostics.QueryDocumentsAsync(typeName, new DocumentQueryOptions(1, 2));
        firstPage.TotalCount.ShouldBe(5);
        firstPage.PageNumber.ShouldBe(1);
        firstPage.PageSize.ShouldBe(2);
        firstPage.DocumentsJson.Count.ShouldBe(2);
        firstPage.DocumentsJson.ShouldAllBe(json => json.Contains("\"Name\""));

        var lastPage = await diagnostics.QueryDocumentsAsync(typeName, new DocumentQueryOptions(3, 2));
        lastPage.TotalCount.ShouldBe(5);
        lastPage.DocumentsJson.Count.ShouldBe(1);

        // The three pages together cover every stored row exactly once.
        var secondPage = await diagnostics.QueryDocumentsAsync(typeName, new DocumentQueryOptions(2, 2));
        firstPage.DocumentsJson
            .Concat(secondPage.DocumentsJson)
            .Concat(lastPage.DocumentsJson)
            .Distinct()
            .Count()
            .ShouldBe(5);
    }

    [Fact]
    public async Task query_documents_can_filter_by_id()
    {
        using var host = await BuildHost("doc_diag_byid", opts => opts.Schema.For<DiagWidget>());

        var target = new DiagWidget { Id = Guid.NewGuid(), Name = "the-one" };
        var store = host.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Store(target);
            session.Store(new DiagWidget { Id = Guid.NewGuid(), Name = "another" });
            await session.SaveChangesAsync();
        }

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(typeof(DiagWidget).FullName!,
            new DocumentQueryOptions(1, 50, target.Id.ToString()));

        result.TotalCount.ShouldBe(1);
        result.DocumentsJson.Single().ShouldContain("the-one");
    }

    [Fact]
    public async Task query_documents_for_an_unknown_type_returns_an_empty_page()
    {
        using var host = await BuildHost("doc_diag_unknown", opts => opts.Schema.For<DiagWidget>());

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync("Some.Type.That.Is.Not.Mapped",
            new DocumentQueryOptions(1, 10));

        result.TotalCount.ShouldBe(0);
        result.DocumentsJson.ShouldBeEmpty();
    }

    [Fact]
    public async Task load_document_json_returns_the_document_or_null()
    {
        using var host = await BuildHost("doc_diag_load", opts => opts.Schema.For<DiagWidget>());

        var target = new DiagWidget { Id = Guid.NewGuid(), Name = "loadable" };
        var store = host.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Store(target);
            await session.SaveChangesAsync();
        }

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
        var typeName = typeof(DiagWidget).FullName!;

        var json = await diagnostics.LoadDocumentJsonAsync(typeName, target.Id.ToString());
        json.ShouldNotBeNull();
        json.ShouldContain("loadable");

        (await diagnostics.LoadDocumentJsonAsync(typeName, Guid.NewGuid().ToString())).ShouldBeNull();
        (await diagnostics.LoadDocumentJsonAsync("Not.A.Mapped.Type", target.Id.ToString())).ShouldBeNull();
    }

    [Fact]
    public async Task mapping_descriptor_carries_subclasses_for_a_hierarchy()
    {
        using var host = await BuildHost("doc_diag_subclasses", opts =>
            opts.Schema.For<DiagAnimal>()
                .AddSubClass<DiagDog>()
                .AddSubClass<DiagCat>());

        var usage = await GetUsageAsync(host);
        var descriptor = usage.Documents.Single(d => d.Alias == "diaganimal");

        descriptor.SubClassCount.ShouldBe(2);
        var subclassNames = descriptor.SubClasses.Select(x => x.Name).ToList();
        subclassNames.ShouldContain(nameof(DiagDog));
        subclassNames.ShouldContain(nameof(DiagCat));
    }

    [Fact]
    public async Task mapping_descriptor_carries_structured_partitioning()
    {
        using var host = await BuildHost("doc_diag_partitioning", opts =>
            opts.Schema.For<Target>().PartitionOn(x => x.Number, x =>
            {
                x.ByRange()
                    .AddRange("low", 0, 10)
                    .AddRange("high", 11, 100);
            }));

        var usage = await GetUsageAsync(host);
        var descriptor = usage.Documents.Single(d => d.Alias == "target");

        descriptor.PartitioningStrategy.ShouldBe("RangePartitioning");
        descriptor.Partitioning.ShouldNotBeNull();
        descriptor.Partitioning!.Strategy.ShouldBe("Range");
        descriptor.Partitioning.PartitionNames.ShouldContain("low");
        descriptor.Partitioning.PartitionNames.ShouldContain("high");
    }

    [Fact]
    public async Task mapping_descriptor_has_no_partitioning_when_not_partitioned()
    {
        using var host = await BuildHost("doc_diag_no_partitioning", opts => opts.Schema.For<DiagWidget>());

        var usage = await GetUsageAsync(host);
        var descriptor = usage.Documents.Single(d => d.Alias == "diagwidget");

        descriptor.PartitioningStrategy.ShouldBeNull();
        descriptor.Partitioning.ShouldBeNull();
    }

    private static async Task<IHost> BuildHost(string schema, Action<StoreOptions> configure)
    {
        // Start from a clean schema so the data-bearing tests get a deterministic row count
        // regardless of prior runs against the same database (e.g. the net9 + net10 matrix).
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = schema;
                    configure(opts);
                });
            })
            .StartAsync();
    }

    private static async Task<DocumentStoreUsage> GetUsageAsync(IHost host)
    {
        var store = (IDocumentStoreUsageSource)host.Services.GetRequiredService<IDocumentStore>();
        var usage = await store.TryCreateUsage(CancellationToken.None);
        usage.ShouldNotBeNull();
        return usage!;
    }
}
