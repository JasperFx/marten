using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core.Migrations;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// #4862 — the store bootstrap swaps <see cref="DefaultTenancy"/> for
/// <see cref="TenantPartitionedSingleDatabaseTenancy"/> ONLY for the
/// "plain single database + <c>UseTenantPartitionedEvents</c>" permutation. The swap lives
/// inside the lazy tenancy factory that <c>StoreOptions.Connection(string)</c> installs, so it
/// resolves after all user configuration has run and can never clobber a user-supplied
/// <see cref="ITenancy"/> (setting <c>opts.Tenancy</c> or using any MultiTenanted* helper
/// replaces that Lazy wholesale). Pure configuration assertions — no store here ever needs
/// provisioned schema.
/// </summary>
public class tenancy_swap_for_tenant_partitioned_single_database
{
    [Fact]
    public void tenant_partitioned_single_database_store_gets_the_dedicated_tenancy()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
        });

        // The subclass relationship matters: every `is DefaultTenancy` behavior gate
        // (AddMartenManagedTenantsAsync routing, DocumentStore.Initialize, coordinator
        // paths) must keep treating this store as a single-database DefaultTenancy store.
        store.Options.Tenancy.ShouldBeOfType<TenantPartitionedSingleDatabaseTenancy>()
            .ShouldBeAssignableTo<DefaultTenancy>();
    }

    [Fact]
    public void plain_single_database_store_keeps_default_tenancy()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
        });

        store.Options.Tenancy.ShouldBeOfType<DefaultTenancy>();
    }

    [Fact]
    public void user_supplied_custom_tenancy_is_never_replaced()
    {
        var custom = new StubTenancy();

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;

            // User-supplied tenancy AFTER Connection() — replaces the lazy default
            // factory entirely, so the #4862 swap never sees it.
            opts.Tenancy = custom;
        });

        store.Options.Tenancy.ShouldBeSameAs(custom);
    }

    // Minimal user-supplied ITenancy: enough for DocumentStore construction (which only
    // touches Default/Cleaner lazily) — everything else throws. Mirrors the documented
    // sample_custom_tenancy shape.
    private class StubTenancy: ITenancy
    {
        public Tenant Default => throw new NotImplementedException();
        public IDocumentCleaner Cleaner => throw new NotImplementedException();
        public DatabaseCardinality Cardinality => DatabaseCardinality.Single;

        public Tenant GetTenant(string tenantId) => throw new NotImplementedException();
        public ValueTask<Tenant> GetTenantAsync(string tenantId) => throw new NotImplementedException();

        public ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier) =>
            throw new NotImplementedException();

        public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId) =>
            throw new NotImplementedException();

        public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases() => throw new NotImplementedException();

        public ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token) =>
            throw new NotImplementedException();

        public void Dispose()
        {
        }
    }
}
