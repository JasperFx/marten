using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests.Partitioning;

// Regression for marten#4493. The reporter combined
// .UseOptimisticConcurrency(true) on a document with the
// AllDocumentsAreMultiTenantedWithPartitioning(...) policy and
// ApplyAllConfiguredChangesToDatabaseAsync threw NullReferenceException
// from UpsertFunction's partition-column scan. Root cause: when
// optimistic concurrency is on a CurrentVersionArgument is added whose
// Column is deliberately null; the partition loop called
// arg.Column.Equals(...) on it. Started on 8.29.0. The reporter
// confirmed setting UseOptimisticConcurrency(false) sidesteps the bug.
//
// Class name kept short on purpose — combined with the
// `mt_doc_{schema}_{type}` table-name prefix and the long
// CoreTests schema base, the 63-byte Postgres identifier limit truncates
// partition table names and collides them. That truncation behavior is
// separate from this issue.
public class Bug_4493_optimistic_partitioning : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_apply_schema_with_optimistic_concurrency_and_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Part>().UseOptimisticConcurrency(true);

            opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(policy =>
            {
                var partitions = policy.ByList();
                partitions.AddPartition("Region1", "Region1");
                partitions.AddPartition("Region2", "Region2");
            });
        });

        // Should not throw — the upsert / overwrite function SQL must be
        // valid PostgreSQL with both optimistic-concurrency and
        // partition-pruning paths in play. Call directly so the
        // underlying NRE is preserved when the bug is in flight.
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);

        // Round-trip a document to confirm the function actually works
        // against the partitioned table, not just that it created.
        await using var session = theStore.LightweightSession("Region1");
        var part = new Part
        {
            Id = Guid.NewGuid(),
            PartNumber = "P-001",
            Cage = "ABC",
            Description = "Regression repro for #4493"
        };
        session.Store(part);
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession("Region1");
        var loaded = await query.LoadAsync<Part>(part.Id);
        loaded.ShouldNotBeNull();
        loaded.PartNumber.ShouldBe("P-001");
    }

    public class Part
    {
        public Guid Id { get; set; }
        public required string PartNumber { get; set; }
        public required string Cage { get; set; }
        public required string Description { get; set; }
    }
}
