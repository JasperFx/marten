using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

/// <summary>
/// Several tenants sharing one partition suffix ("bucketing") under Marten-managed partitioning, with Issue
/// referencing User through a foreign key
/// </summary>
public class managed_tenant_partition_buckets : OneOffConfigurationsContext
{
    private async Task<IReadOnlyDictionary<string, string>> bucketedStoreAsync()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(SchemaName);

            opts.Schema.For<User>();
            opts.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,
            new Dictionary<string, string> { ["red"] = "warm", ["orange"] = "warm", ["blue"] = "blue" });

        await storeUserAndIssueAsync("red");
        await storeUserAndIssueAsync("orange");
        await storeUserAndIssueAsync("blue");

        return martenView();
    }

    private IReadOnlyDictionary<string, string> martenView()
    {
        return theStore.Options.TenantPartitions!.Partitions
            .PartitionsFor((PostgresqlDatabase)theStore.Storage.Database);
    }

    private async Task storeUserAndIssueAsync(string tenantId)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = tenantId };
        var issue = new Issue { Id = Guid.NewGuid(), Title = tenantId, AssigneeId = user.Id };

        await using var session = theStore.LightweightSession(tenantId);
        session.Store(user);
        session.Store(issue);
        await session.SaveChangesAsync();
    }

    private async Task<int> issueCountAsync(string tenantId)
    {
        await using var session = theStore.QuerySession(tenantId);
        return await session.Query<Issue>().CountAsync();
    }

    private async Task<string[]> suffixesOf(Type documentType)
    {
        var table = await theStore.Storage.Database.ExistingTableFor(documentType);
        return table.Partitioning.ShouldBeOfType<ListPartitioning>().Partitions.Select(x => x.Suffix)
            .OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task deleting_one_tenant_of_a_shared_bucket_keeps_the_other_tenants_in_it()
    {
        await bucketedStoreAsync();

        await theStore.Advanced.DeleteAllTenantDataAsync("red", CancellationToken.None);

        (await issueCountAsync("red")).ShouldBe(0);
        (await issueCountAsync("orange")).ShouldBe(1);
        (await issueCountAsync("blue")).ShouldBe(1);

        (await suffixesOf(typeof(User))).ShouldBe(["blue", "warm"]);
        (await suffixesOf(typeof(Issue))).ShouldBe(["blue", "warm"]);

        martenView().ShouldBe(new Dictionary<string, string> { ["orange"] = "warm", ["blue"] = "blue" },
            ignoreOrder: true);
    }

    [Fact]
    public async Task re_adding_a_tenant_under_another_suffix_records_the_bucket_it_went_back_into()
    {
        await bucketedStoreAsync();
        await theStore.Advanced.DeleteAllTenantDataAsync("red", CancellationToken.None);

        // mt_doc_user_warm is referenced by mt_doc_issue, so its bound still covers "red" (weasel#891), and
        // Weasel puts "red" back into the "warm" bucket instead of a new "red" partition (weasel#896)
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,
            new Dictionary<string, string> { ["red"] = "red" });

        (await suffixesOf(typeof(User))).ShouldBe(["blue", "warm"]);
        (await suffixesOf(typeof(Issue))).ShouldBe(["blue", "warm"]);

        // Marten's own view of the database holds the suffix Weasel used, not the one asked for
        martenView()["red"].ShouldBe("warm");

        await storeUserAndIssueAsync("red");
        (await issueCountAsync("red")).ShouldBe(1);
        (await issueCountAsync("orange")).ShouldBe(1);
    }

    [Fact]
    public async Task deleting_one_tenant_of_a_shared_bucket_keeps_the_buckets_event_sequence()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(SchemaName);

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType(typeof(BucketedWidgetMade));
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,
            new Dictionary<string, string> { ["red"] = "warm", ["orange"] = "warm" });

        await appendWidgetAsync("red");
        await appendWidgetAsync("orange");

        await theStore.Advanced.DeleteAllTenantDataAsync("red", CancellationToken.None);

        // "orange" still draws from mt_events_sequence_warm
        await appendWidgetAsync("orange");

        await using var orange = theStore.QuerySession("orange");
        (await orange.Events.QueryAllRawEvents().CountAsync()).ShouldBe(2);
    }

    private async Task appendWidgetAsync(string tenantId)
    {
        await using var session = theStore.LightweightSession(tenantId);
        session.Events.StartStream(Guid.NewGuid(), new BucketedWidgetMade(tenantId));
        await session.SaveChangesAsync();
    }
}

public record BucketedWidgetMade(string Name);
