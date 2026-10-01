using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Documents;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
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
/// <remarks>
/// #5543 / jasperfx#870 grew that surface and defined its semantics. The semantics themselves are pinned
/// cross-store by <c>DocumentStoreDiagnosticsCompliance</c> (soft deletes, hierarchies, tenancy, id
/// conversion, criteria refusal, and the write sibling), so what is added here is only what is Marten's
/// own: the writer's DI registration, the serializer-casing and index/duplicated-field descriptors, and
/// the rule that a diagnostic read must never PROVISION a tenant's database.
/// </remarks>
public class document_store_diagnostics_tests: HostedStoreContext
{
    [Fact]
    public async Task diagnostics_is_registered_in_the_container()
    {
        var host = await BuildHost("di", opts => opts.Schema.For<DiagWidget>());

        host.Services.GetService<IDocumentStoreDiagnostics>().ShouldNotBeNull();
    }

    [Fact]
    public async Task document_types_lists_registered_mappings()
    {
        var host = await BuildHost("types", opts =>
        {
            opts.Schema.For<DiagWidget>();
            opts.Schema.For<User>();
        });

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
        var types = await diagnostics.DocumentTypesAsync(CancellationToken.None);

        var widget = types.Single(t => t.Alias == "diagwidget");
        widget.TypeName.ShouldContain(nameof(DiagWidget));
        widget.SchemaName.ShouldBe($"{SchemaName}_types");

        types.Select(t => t.Alias).ShouldContain("user");
    }

    [Fact]
    public async Task query_documents_pages_and_reports_the_total()
    {
        var host = await BuildHost("paging", opts => opts.Schema.For<DiagWidget>());

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
        var host = await BuildHost("byid", opts => opts.Schema.For<DiagWidget>());

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
        var host = await BuildHost("unknown", opts => opts.Schema.For<DiagWidget>());

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync("Some.Type.That.Is.Not.Mapped",
            new DocumentQueryOptions(1, 10));

        result.TotalCount.ShouldBe(0);
        result.DocumentsJson.ShouldBeEmpty();
    }

    [Fact]
    public async Task load_document_json_returns_the_document_or_null()
    {
        var host = await BuildHost("load", opts => opts.Schema.For<DiagWidget>());

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

    /// <remarks>
    /// jasperfx#932 settled a cross-store disagreement -- Polecat listed sub-classes, Marten and Fisher
    /// did not -- in favour of listing them, since every member taking a documentTypeName already accepts
    /// a sub-class name. DocumentStoreDiagnosticsCompliance pins the listing and the root marker; what is
    /// Marten's own, and asserted here, is that a sub-class carries its own mt_doc_type alias and the
    /// ROOT's schema, because that is where its rows actually live.
    /// </remarks>
    [Fact]
    public async Task document_types_list_subclasses_against_the_root_table()
    {
        var host = await BuildHost("subclasstypes", opts =>
            opts.Schema.For<DiagAnimal>()
                .AddSubClass<DiagDog>()
                .AddSubClass<DiagCat>());

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
        var types = (await diagnostics.DocumentTypesAsync(CancellationToken.None))
            .ToDictionary(x => x.TypeName);

        var root = types.Values.Single(x => x.Alias == "diaganimal");
        root.RootTypeName.ShouldBeNull();
        root.IsSubClass.ShouldBeFalse();

        // The alias is the mt_doc_type discriminator the rows actually carry, not the type name
        // lower-cased -- that is the string a picker hands back to QueryDocumentsAsync.
        foreach (var (subClassType, alias) in new[] { (typeof(DiagDog), "diag_dog"), (typeof(DiagCat), "diag_cat") })
        {
            var subClass = types[subClassType.FullNameInCode()];

            subClass.IsSubClass.ShouldBeTrue();
            subClass.RootTypeName.ShouldBe(root.TypeName);
            subClass.Alias.ShouldBe(alias);
            subClass.SchemaName.ShouldBe(root.SchemaName);
        }
    }

    [Fact]
    public async Task mapping_descriptor_carries_subclasses_for_a_hierarchy()
    {
        var host = await BuildHost("subclasses", opts =>
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
        var host = await BuildHost("partitioning", opts =>
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
        var host = await BuildHost("no_partitioning", opts => opts.Schema.For<DiagWidget>());

        var usage = await GetUsageAsync(host);
        var descriptor = usage.Documents.Single(d => d.Alias == "diagwidget");

        descriptor.PartitioningStrategy.ShouldBeNull();
        descriptor.Partitioning.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------------------------
    // #5543 / jasperfx#870. The cross-store semantics live in DocumentStoreDiagnosticsCompliance; what
    // is Marten's alone goes here.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task the_writer_is_registered_in_the_container()
    {
        var host = await BuildHost("writer_di", opts => opts.Schema.For<DiagWidget>());

        host.Services.GetService<IDocumentStoreDiagnosticsWriter>().ShouldNotBeNull();
    }

    [Fact]
    public async Task usage_advertises_the_serializer_casing()
    {
        var host = await BuildHost("casing", opts =>
        {
            opts.Schema.For<DiagWidget>();
            opts.UseSystemTextJsonForSerialization(casing: Casing.CamelCase);
        });

        var usage = await GetUsageAsync(host);

        // jasperfx#870 §5: without this a console cannot tell "the document has no Status member" from
        // "the document stores it as `status`" -- both read as a miss against the same path.
        usage.SerializerCasing.ShouldBe(nameof(Casing.CamelCase));
    }

    [Fact]
    public async Task mapping_descriptor_carries_duplicated_fields_and_indexes()
    {
        var host = await BuildHost("duplicated", opts =>
            opts.Schema.For<Target>().Duplicate(x => x.Number));

        var usage = await GetUsageAsync(host);
        var descriptor = usage.Documents.Single(d => d.Alias == "target");

        var duplicated = descriptor.DuplicatedFields.ShouldHaveSingleItem();
        duplicated.MemberPath.ShouldBe(nameof(Target.Number));
        duplicated.ColumnName.ShouldBe("number");
        duplicated.DbType.ShouldNotBeNullOrWhiteSpace();

        // Duplicating a member also indexes it, and the index is the reason the structured list exists:
        // a console asking "can I filter on Number cheaply?" gets an answer without parsing Ddl. The
        // member path comes back too, which it can only do for a duplicated field's own column.
        var index = descriptor.Indexes.ShouldHaveSingleItem();
        index.Columns.ShouldContain("number");
        index.Members.ShouldContain(nameof(Target.Number));
        index.Name.ShouldNotBeNullOrWhiteSpace();
        index.Method.ShouldBe("btree");
    }

    [Fact]
    public async Task a_query_for_an_unknown_tenant_does_not_provision_anything()
    {
        // The trap #5400 recorded on the event-store explorer, in the same shape: Tenancy's
        // FindOrCreateDatabase is not a lookup -- on SingleServerMultiTenancy it issues CREATE DATABASE.
        // A read-only console question about a tenant that does not exist must not bring that tenant into
        // being, so diagnostics goes through TryFindDatabase and answers with an empty page.
        var schema = $"{SchemaName}_unknown_tenant";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
            await conn.RunSqlAsync("drop database if exists diag_unknown_tenant_probe with (force)");
        }

        using var host = await StartHostAsync(opts =>
        {
            opts.DatabaseSchemaName = schema;
            opts.MultiTenantedDatabases(x =>
                x.AddSingleTenantDatabase(ConnectionSource.ConnectionString, "diag_known"));
            opts.Schema.For<DiagWidget>();
        });

        var diagnostics = (IDocumentStoreDiagnostics)host.Services.GetRequiredService<IDocumentStore>();
        var typeName = typeof(DiagWidget).FullName!;

        // An unknown TENANT is refused rather than read as empty, and the two really are different
        // questions: "this tenant has no rows of that type" is an answer, "there is no such tenant" is a
        // mistake the operator should see. That matches findExplorerDatabaseAsync on the event-store side.
        // An unknown TYPE still reads as empty -- that one the contract does specify.
        await Should.ThrowAsync<UnknownTenantIdException>(() => diagnostics.QueryDocumentsAsync(typeName,
            new DocumentQueryOptions(1, 10) { TenantId = "diag_unknown_tenant_probe" }));

        await Should.ThrowAsync<UnknownTenantIdException>(() => diagnostics.LoadDocumentAsync(
            typeName, Guid.NewGuid().ToString(), "diag_unknown_tenant_probe"));

        await using var check = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await check.OpenAsync();
        (await check.DatabaseExists("diag_unknown_tenant_probe")).ShouldBeFalse();
    }

    [Fact]
    public async Task all_tenants_across_several_databases_is_refused_not_narrowed()
    {
        // #5544 / jasperfx#928. The compliance suite cannot reach this: its fixture builds a
        // single-database conjoined store, which is the arm Marten DOES answer. A store spreading tenants
        // over several databases is the arm Marten refuses, and the refusal is the contract rather than a
        // gap -- answering from the default database would hand a console the default tenant's rows as
        // though they were every tenant's, and nothing in the result would say otherwise.
        var schema = $"{SchemaName}_alltenants_refused";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        using var host = await StartHostAsync(opts =>
        {
            opts.DatabaseSchemaName = schema;
            opts.MultiTenantedDatabases(x =>
                x.AddSingleTenantDatabase(ConnectionSource.ConnectionString, "diag_alltenants"));
            opts.Schema.For<DiagWidget>();
        });

        var diagnostics = (IDocumentStoreDiagnostics)host.Services.GetRequiredService<IDocumentStore>();

        var refused = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(
            () => diagnostics.QueryDocumentsAsync(typeof(DiagWidget).FullName!,
                new DocumentQueryOptions(1, 10) { AllTenants = true }));

        refused.Criterion.ShouldBe(nameof(DocumentQueryOptions.AllTenants));
    }

    [Fact]
    public async Task all_tenants_combined_with_a_named_tenant_is_rejected_before_the_store_is_consulted()
    {
        // Asserted on the multi-database store deliberately: the contradiction has to be caught by
        // AssertValidTenantScope BEFORE Marten decides whether it can honour AllTenants at all, or a store
        // that refuses the fan-out would report the wrong complaint -- DocumentCriteriaNotSupportedException
        // for a request that is malformed rather than unsupported.
        var schema = $"{SchemaName}_alltenants_contradiction";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        using var host = await StartHostAsync(opts =>
        {
            opts.DatabaseSchemaName = schema;
            opts.MultiTenantedDatabases(x =>
                x.AddSingleTenantDatabase(ConnectionSource.ConnectionString, "diag_contradiction"));
            opts.Schema.For<DiagWidget>();
        });

        var diagnostics = (IDocumentStoreDiagnostics)host.Services.GetRequiredService<IDocumentStore>();

        await Should.ThrowAsync<ArgumentException>(
            () => diagnostics.QueryDocumentsAsync(typeof(DiagWidget).FullName!,
                new DocumentQueryOptions(1, 10) { AllTenants = true, TenantId = "diag_contradiction" }));
    }

    private async Task<IHost> BuildHost(string suffix, Action<StoreOptions> configure)
    {
        // Start from a clean schema so the data-bearing tests get a deterministic row count
        // regardless of prior runs against the same database (e.g. the net9 + net10 matrix).
        var schema = $"{SchemaName}_{suffix}";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        return await StartHostAsync(opts =>
        {
            opts.DatabaseSchemaName = schema;
            configure(opts);
        });
    }

    private static async Task<DocumentStoreUsage> GetUsageAsync(IHost host)
    {
        var store = (IDocumentStoreUsageSource)host.Services.GetRequiredService<IDocumentStore>();
        var usage = await store.TryCreateUsage(CancellationToken.None);
        usage.ShouldNotBeNull();
        return usage!;
    }
}
