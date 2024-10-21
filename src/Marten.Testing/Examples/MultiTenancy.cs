using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Examples;

public class MultiTenancy
{
    public static void configuring_tenant_id_rules()
    {
        #region sample_using_tenant_id_style

        var store = DocumentStore.For(opts =>
        {
            // This is the default
            opts.TenantIdStyle = TenantIdStyle.CaseSensitive;

            // Or opt into this behavior:
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;

            // Or force all tenant ids to be converted to upper case internally
            opts.TenantIdStyle = TenantIdStyle.ForceUpperCase;
        });

        #endregion
    }

    [Fact]
    public async Task use_multiple_tenants()
    {
        // Set up a basic DocumentStore with multi-tenancy
        // via a tenant_id column
        var store = DocumentStore.For(storeOptions =>
        {
            // Bookkeeping;)
            storeOptions.DatabaseSchemaName = "multi1";

            // This sets up the DocumentStore to be multi-tenanted
            // by a tenantid column
            storeOptions.Connection(ConnectionSource.ConnectionString);

            #region sample_tenancy-configure-through-policy

            storeOptions.Policies.AllDocumentsAreMultiTenanted();
            // Shorthand for
            // storeOptions.Policies.ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);

            #endregion
        });

        store.Advanced.Clean.CompletelyRemoveAll();

        #region sample_tenancy-scoping-session-write

        // Write some User documents to tenant "tenant1"
        using (var session = store.LightweightSession("tenant1"))
        {
            session.Store(new User { UserName = "Bill" });
            session.Store(new User { UserName = "Lindsey" });
            await session.SaveChangesAsync();
        }

        #endregion

        // Write some User documents to tenant "tenant2"
        using (var session = store.LightweightSession("tenant2"))
        {
            session.Store(new User { UserName = "Jill" });
            session.Store(new User { UserName = "Frank" });
            await session.SaveChangesAsync();
        }

        #region sample_tenancy-scoping-session-read

        // When you query for data from the "tenant1" tenant,
        // you only get data for that tenant
        using (var query = store.QuerySession("tenant1"))
        {
            query.Query<User>()
                .Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("Bill", "Lindsey");
        }

        #endregion

        using (var query = store.QuerySession("tenant2"))
        {
            query.Query<User>()
                .Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("Jill", "Frank");
        }
    }

    [Fact]
    public async Task use_multiple_tenants_with_partitioning()
    {
        // Set up a basic DocumentStore with multi-tenancy
        // via a tenant_id column
        var store = DocumentStore.For(storeOptions =>
        {
            // Bookkeeping;)
            storeOptions.DatabaseSchemaName = "multi1";

            // This sets up the DocumentStore to be multi-tenanted
            // by a tenantid column
            storeOptions.Connection(ConnectionSource.ConnectionString);

            #region sample_tenancy-configure-through-policy_with_partitioning

            storeOptions.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x =>
            {
                // Selectively by LIST partitioning
                x.ByList()
                    // Adding explicit table partitions for specific tenant ids
                    .AddPartition("t1", "T1")
                    .AddPartition("t2", "T2");

                // OR Use LIST partitioning, but allow the partition tables to be
                // controlled outside of Marten by something like pg_partman
                // https://github.com/pgpartman/pg_partman
                x.ByExternallyManagedListPartitions();

                // OR Just spread out the tenant data by tenant id through
                // HASH partitioning
                // This is using three different partitions with the supplied
                // suffix names
                x.ByHash("one", "two", "three");

                // OR Partition by tenant id based on ranges of tenant id values
                x.ByRange()
                    .AddRange("north_america", "na", "nazzzzzzzzzz")
                    .AddRange("asia", "a", "azzzzzzzz");

                // OR use RANGE partitioning with the actual partitions managed
                // externally
                x.ByExternallyManagedRangePartitions();
            });

            #endregion
        });

        store.Advanced.Clean.CompletelyRemoveAll();

        #region sample_tenancy-scoping-session-write_with_partitioning

        // Write some User documents to tenant "tenant1"
        using (var session = store.LightweightSession("tenant1"))
        {
            session.Store(new User { UserName = "Bill" });
            session.Store(new User { UserName = "Lindsey" });
            await session.SaveChangesAsync();
        }

        #endregion

        // Write some User documents to tenant "tenant2"
        using (var session = store.LightweightSession("tenant2"))
        {
            session.Store(new User { UserName = "Jill" });
            session.Store(new User { UserName = "Frank" });
            await session.SaveChangesAsync();
        }

        #region sample_tenancy-scoping-session-read_with_partitioning

        // When you query for data from the "tenant1" tenant,
        // you only get data for that tenant
        using (var query = store.QuerySession("tenant1"))
        {
            query.Query<User>()
                .Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("Bill", "Lindsey");
        }

        #endregion

        using (var query = store.QuerySession("tenant2"))
        {
            query.Query<User>()
                .Select(x => x.UserName)
                .ToList()
                .ShouldHaveTheSameElementsAs("Jill", "Frank");
        }
    }

    public static void using_partitioning_on_a_single_document()
    {
        #region sample_configure_partitioning_on_single_table

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            opts.Schema.For<User>().MultiTenantedWithPartitioning(x =>
            {
                x.ByExternallyManagedListPartitions();
            });
        });

        #endregion
    }

    public static void using_partitioning_for_all_tenanted_documents()
    {
        #region sample_multi_tenancy_partitioning_policy

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // This document type is global, so no tenancy
            opts.Schema.For<Region>().SingleTenanted();

            // We want these document types to be tenanted
            opts.Schema.For<Invoice>().MultiTenanted();
            opts.Schema.For<User>().MultiTenanted();

            // Apply table partitioning by tenant id to each document type
            // that is using conjoined multi-tenancy
            opts.Policies.PartitionMultiTenantedDocuments(x =>
            {
                x.ByExternallyManagedListPartitions();
            });
        });

        #endregion
    }
}

public class Region
{
    public Guid Id { get; set; }
}
