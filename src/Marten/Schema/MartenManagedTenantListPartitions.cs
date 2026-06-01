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

    public MartenManagedTenantListPartitions(StoreOptions options, string? schemaName)
    {
        _options = options;

        // #4596 Session 2: stash the qualified DbObjectName on the wrapper so the
        // QuickAppendEventFunction can reach it for the in-function
        // `SELECT partition_suffix FROM <schema>.mt_tenant_partitions WHERE
        // partition_value = tenantid` lookup. ManagedListPartitions keeps its
        // internal Table private — the wrapper is the public access point.
        TenantsTableName = new DbObjectName(schemaName ?? options.DatabaseSchemaName, TableName);

        Partitions = new ManagedListPartitions("TenantIds", TenantsTableName);

        _options.Storage.Add(Partitions);

        _options.TenantPartitions = this;
    }

    public ManagedListPartitions Partitions { get; }

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
