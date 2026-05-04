using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// Coverage for <c>IDocumentStoreUsageSource.TryCreateUsage</c> on Marten's
/// <see cref="DocumentStore"/>. Verifies the descriptor population pass —
/// first-class properties, flat OptionValues, code-generation child, and
/// per-document-type mappings with their generated DDL — drives the
/// CritterWatch Documents tab end-to-end so the operationally-interesting
/// settings reach the monitoring console accurately.
/// </summary>
public class document_store_usage_tests
{
    [Fact]
    public async Task usage_carries_first_class_identity_properties()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_identity";
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Schema.For<User>();
        });

        var usage = await GetUsageAsync(host);

        usage.ShouldNotBeNull();
        usage.SubjectUri.ShouldBe(new Uri("marten://main"));
        usage.StoreName.ShouldBe("Main");
        usage.DatabaseSchemaName.ShouldBe("doc_usage_identity");
        usage.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None.ToString());
        usage.EnumStorage.ShouldNotBeNullOrEmpty();
        usage.Version.ShouldNotBeNullOrEmpty();
        usage.Database.ShouldNotBeNull();
    }

    [Fact]
    public async Task usage_includes_a_descriptor_per_registered_document_mapping()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_mappings";
            opts.Schema.For<User>();
            opts.Schema.For<Issue>();
            opts.Schema.For<Target>();
        });

        var usage = await GetUsageAsync(host);

        var aliases = usage.Documents.Select(d => d.Alias).ToList();
        aliases.ShouldContain("user");
        aliases.ShouldContain("issue");
        aliases.ShouldContain("target");

        var userMapping = usage.Documents.Single(d => d.Alias == "user");
        userMapping.DocumentType.FullName.ShouldBe(typeof(User).FullName);
        userMapping.DocumentType.Name.ShouldBe(nameof(User));
        userMapping.DatabaseSchemaName.ShouldBe("doc_usage_mappings");
        userMapping.IdStrategy.ShouldNotBeNullOrEmpty();
        userMapping.TenancyStyle.ShouldBe("Single");
        userMapping.DeleteStyle.ShouldBe("Remove");
    }

    [Fact]
    public async Task mapping_descriptor_carries_concurrency_and_tenancy_overrides()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_overrides";
            opts.Schema.For<User>().UseOptimisticConcurrency(true);
            opts.Schema.For<Issue>().MultiTenanted();
            opts.Schema.For<Target>().SoftDeleted();
        });

        var usage = await GetUsageAsync(host);

        usage.Documents.Single(d => d.Alias == "user").UseOptimisticConcurrency.ShouldBeTrue();
        usage.Documents.Single(d => d.Alias == "issue").TenancyStyle.ShouldBe("Conjoined");
        usage.Documents.Single(d => d.Alias == "target").DeleteStyle.ShouldBe("SoftDelete");
    }

    [Fact]
    public async Task mapping_descriptor_carries_full_create_table_ddl()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_ddl";
            opts.Schema.For<User>();
        });

        var usage = await GetUsageAsync(host);
        var mapping = usage.Documents.Single(d => d.Alias == "user");

        // The DDL field is the canonical "what schema gets applied" view —
        // operators on the CritterWatch Documents tab can copy/paste this
        // straight into a SQL console. It must contain the CREATE TABLE
        // statement for this mapping at minimum.
        mapping.Ddl.ShouldNotBeNullOrEmpty();
        mapping.Ddl.ShouldContain("CREATE", Case.Insensitive);
        mapping.Ddl.ShouldContain("doc_usage_ddl.mt_doc_user", Case.Insensitive);
    }

    [Fact]
    public async Task usage_carries_flat_option_values_for_secondary_settings()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_flat";
            opts.CommandTimeout = 42;
            opts.UpdateBatchSize = 250;
            opts.Schema.For<User>();
        });

        var usage = await GetUsageAsync(host);

        // CommandTimeout and UpdateBatchSize are lifted onto the flat
        // Properties bag (Cluster C). The bag is hand-populated, so each
        // expected key must appear with the right value.
        usage.PropertyFor(nameof(StoreOptions.CommandTimeout))!.RawValue.ShouldBe(42);
        usage.PropertyFor(nameof(StoreOptions.UpdateBatchSize))!.RawValue.ShouldBe(250);

        // Cluster H6: HiloMaxLo lifted from HiloSequenceDefaults.
        usage.PropertyFor("HiloMaxLo").ShouldNotBeNull();

        // Cluster H7: ReadSessionPreference / WriteSessionPreference lifted
        // from MultiHostSettings.
        usage.PropertyFor("ReadSessionPreference").ShouldNotBeNull();
        usage.PropertyFor("WriteSessionPreference").ShouldNotBeNull();
    }

    [Fact]
    public async Task usage_includes_code_generation_child_descriptor()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_codegen";
            opts.Schema.For<User>();
        });

        var usage = await GetUsageAsync(host);

        usage.CodeGeneration.ShouldNotBeNull();
        usage.CodeGeneration.GeneratedCodeMode.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task event_store_usage_includes_global_aggregates_when_present()
    {
        using var host = await BuildHost(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "doc_usage_global_aggs";
            // GlobalAggregates lives on the internal EventGraph implementation
            // (not on the public IEventStoreOptions surface). CoreTests has
            // InternalsVisibleTo, so the cast is fine here.
            opts.EventGraph.GlobalAggregates.Add(typeof(User));
        });

        var usage = await host.Services.GetRequiredService<IEventStore>()
            .TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.GlobalAggregates.ShouldContain(g => g.FullName == typeof(User).FullName);
    }

    private static async Task<IHost> BuildHost(Action<StoreOptions> configure)
    {
        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(configure);
            })
            .StartAsync();
    }

    private static async Task<JasperFx.Descriptors.DocumentStoreUsage> GetUsageAsync(IHost host)
    {
        var store = (IDocumentStoreUsageSource)host.Services.GetRequiredService<IDocumentStore>();
        var usage = await store.TryCreateUsage(CancellationToken.None);
        usage.ShouldNotBeNull();
        return usage!;
    }
}
