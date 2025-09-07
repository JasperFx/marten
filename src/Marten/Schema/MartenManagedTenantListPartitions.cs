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

        Partitions = new ManagedListPartitions("TenantIds",
            new DbObjectName(schemaName ?? options.DatabaseSchemaName, TableName));

        _options.Storage.Add(Partitions);

        _options.TenantPartitions = this;
    }

    public ManagedListPartitions Partitions { get; }

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
