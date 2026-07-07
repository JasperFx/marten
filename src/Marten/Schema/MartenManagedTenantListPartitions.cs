using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using Marten.Events;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Schema;

public class MartenManagedTenantListPartitions : IDocumentPolicy
{
    private readonly StoreOptions _options;
    public const string TableName = "mt_tenant_partitions";

    /// <summary>
    /// Reserved partition suffix backing the default-tenant (<c>*DEFAULT*</c>) slot that
    /// global projections (<c>AddGlobalProjection</c>) write to under
    /// <c>Events.UseTenantPartitionedEvents</c>. The sentinel tenant id itself contains
    /// characters that are illegal in PostgreSQL identifiers so it can never be its own
    /// partition-table suffix — but a LIST partition VALUE can be any string, so the
    /// default tenant's rows live in partitions named with this fixed, identifier-legal
    /// suffix instead (<c>mt_events___default__</c> etc.). Auto-provisioned by
    /// <c>AdvancedOperations.AddMartenManagedTenantsAsync</c> whenever the store has
    /// global aggregates registered. See https://github.com/JasperFx/marten/issues/4648.
    /// </summary>
    public const string DefaultTenantSuffix = "__default__";

    public MartenManagedTenantListPartitions(StoreOptions options, string? schemaName)
    {
        _options = options;

        // #4596 Session 2: stash the qualified DbObjectName on the wrapper so the
        // QuickAppendEventFunction can reach it for the in-function
        // `SELECT partition_suffix FROM <schema>.mt_tenant_partitions WHERE
        // partition_value = tenantid` lookup. ManagedListPartitions keeps its
        // internal Table private — the wrapper is the public access point.
        TenantsTableName = new DbObjectName(schemaName ?? options.DatabaseSchemaName, TableName);

        // #4863/#4855: the database-scoped subclass keys the expected partition set per database,
        // hydrated from each database's own mt_tenant_partitions — the plain Weasel
        // ManagedListPartitions keeps one store-wide snapshot, which is wrong the moment a store
        // spans multiple databases (sharded pools, master-table tenancy).
        Partitions = new DatabaseScopedTenantPartitions("TenantIds", TenantsTableName);

        _options.Storage.Add(Partitions);

        _options.TenantPartitions = this;
    }

    public DatabaseScopedTenantPartitions Partitions { get; }

    /// <summary>
    /// Fully qualified name of the <c>mt_tenant_partitions</c> table this
    /// manager owns. Surfaced for downstream consumers (per-tenant event
    /// sequence machinery in #4596 etc.) so they can emit SQL that references
    /// the lookup table regardless of the schema the user configured.
    /// </summary>
    public DbObjectName TenantsTableName { get; }

    public void Apply(DocumentMapping mapping)
    {
        if (mapping is EventQueryMapping) return;

        if (mapping.TenancyStyle == TenancyStyle.Single) return;
        if (mapping.DocumentType.HasAttribute<SingleTenantedAttribute>()) return;
        if (mapping.DisablePartitioningIfAny) return;
        if (mapping.DocumentType == typeof(DeadLetterEvent)) return;

        mapping.Partitioning =
            new ListPartitioning { Columns = [TenantIdColumn.Name] }.UsePartitionManager(Partitions);
    }
}
