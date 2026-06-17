using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Events.Archiving;
using Marten.Linq.Parsing;
using Marten.Storage;
using Marten.Storage.Metadata;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Events.Schema;

internal class StreamsTable: Table
{
    public const string TableName = "mt_streams";

    public StreamsTable(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, TableName))
    {
        foreach (var index in events.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        // Per https://github.com/JasperFx/marten/issues/2430, this needs to be first in the PK
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        var idColumn = events.StreamIdentity == StreamIdentity.AsGuid
            ? new StreamTableColumn("id", x => x.Id)
            : new StreamTableColumn("id", x => x.Key);

        AddColumn(idColumn).AsPrimaryKey();
        AddColumn(new StreamTableColumn("type", x => x.AggregateTypeName)).AllowNulls();

        AddColumn(new StreamTableColumn("version", x => x.Version)).AllowNulls();

        AddColumn(new StreamTableColumn("timestamp", x => x.Timestamp)
        {
            Type = "timestamptz", Writes = false, AllowNulls = false, DefaultExpression = "(now())"
        });

        // Note: the `snapshot` (jsonb) and `snapshot_version` (integer) columns
        // were vestigial holdovers from pre-1.0 Marten and were never actually
        // populated or read at runtime. They were dropped in Marten 9.0.
        // The 8→9 migration guide explains how to drop them from existing
        // databases (no data migration required).

        AddColumn(new StreamTableColumn("created", x => x.Created)
        {
            Type = "timestamptz", Writes = false, AllowNulls = false, DefaultExpression = "(now())"
        });

        if (events.TenancyStyle != TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>();
        }

        var archiving = AddColumn<IsArchivedColumn>();
        if (events.UseArchivedStreamPartitioning)
        {
            archiving.PartitionByListValues().AddPartition("archived", true);
        }

        // #4596 Session 1: per-tenant partitioning of mt_streams via the existing
        // ManagedListPartitions instance. tenant_id is already part of the PK in
        // conjoined mode (see top of constructor — first column for partition
        // affinity). Combination with archived-partitioning is rejected at
        // config time; tenancy style restriction is enforced upstream by the
        // requirement that callers pass real tenant_ids on append.
        if (events.UseTenantPartitionedEvents)
        {
            var manager = events.Options.TenantPartitions!.Partitions;
            Partitioning = new ListPartitioning { Columns = [TenantIdColumn.Name] }
                .UsePartitionManager(manager);

            // #4753 (mirrors #4706 for DocumentTable): exempt this Marten-managed LIST partitioning
            // from child-partition reconciliation in the generic schema diff. The per-tenant
            // partitions are created out-of-band by AddMartenManagedTenantsAsync /
            // AddTenantToShardAsync. Without this, re-applying an unchanged schema over existing data
            // sees the live partitions as "unexpected" and destructively rebuilds mt_streams,
            // failing with 23514 because the rebuilt parent has no partitions yet.
            IgnorePartitionsInMigration = true;
        }
    }
}

internal interface IStreamTableColumn
{
    bool Reads { get; }
    bool Writes { get; }

    string Name { get; }
}

internal class StreamTableColumn: TableColumn, IStreamTableColumn
{
    private readonly MemberInfo _member;
    private readonly Expression<Func<StreamAction, object>> _memberExpression;

    public StreamTableColumn(string name, Expression<Func<StreamAction, object>> memberExpression): base(name,
        "varchar")
    {
        _memberExpression = memberExpression;
        _member = MemberFinder.Determine(memberExpression).Single();
        var memberType = _member.GetMemberType();
        Type = PostgresqlProvider.Instance.GetDatabaseType(memberType, EnumStorage.AsInteger);
        NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(memberType);
    }

    public NpgsqlDbType NpgsqlDbType { get; set; }

    public bool Reads { get; set; } = true;
    public bool Writes { get; set; } = true;
}
