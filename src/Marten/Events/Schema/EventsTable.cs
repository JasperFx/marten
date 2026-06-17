using System.Collections.Generic;
using System.Linq;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Events.Schema;

#region sample_EventsTable

internal class EventsTable: Table
{
    public EventsTable(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events"))
    {
        foreach (var index in events.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn(new SequenceColumn()).AsPrimaryKey();
        AddColumn(new EventTableColumn("id", x => x.Id)).NotNull();
        AddColumn(new StreamIdColumn(events));

        AddColumn(new VersionColumn());
        AddColumn<EventJsonDataColumn>();
        // #4515: nullable sibling to `data` for binary event payloads. NULL
        // for JSON-serialized events; bytes (e.g. MemoryPack) for events
        // opted in via [BinaryEvent] / opts.Events.UseBinarySerializer<T>(...).
        AddColumn<EventBdataColumn>();
        AddColumn<EventTypeColumn>();
        AddColumn(new EventTableColumn("timestamp", x => x.Timestamp))
            .NotNull().DefaultValueByString("(now())");

        // #4596 Session 1: PostgreSQL requires the partition column in every
        // unique constraint on a partitioned table, PK included. When
        // UseTenantPartitionedEvents is on, mark tenant_id as part of the PK
        // alongside seq_id (which alone is already store-unique via the
        // sequence — adding tenant_id is purely about satisfying the PG rule).
        var tenantIdColumn = AddColumn<TenantIdColumn>();
        if (events.UseTenantPartitionedEvents)
        {
            tenantIdColumn.AsPrimaryKey();
        }

        AddColumn<DotNetTypeColumn>().AllowNulls();

        AddIfActive(events.Metadata.CorrelationId);
        AddIfActive(events.Metadata.CausationId);
        AddIfActive(events.Metadata.Headers);
        AddIfActive(events.Metadata.UserName);

        if (events.EnableEventSkippingInProjectionsOrSubscriptions)
        {
            AddColumn<bool>("is_skipped").DefaultValueByExpression("FALSE");
        }

        // DCB HStore mode: tag key-value pairs live inline on the event row, and a
        // single GIN index covers all tag types via the @> containment operator.
        if (events.DcbStorageMode == DcbStorageMode.HStore)
        {
            AddColumn("tags", "hstore").AllowNulls();
            Indexes.Add(new IndexDefinition("idx_mt_events_tags")
            {
                Method = IndexMethod.gin,
                Columns = ["tags"]
            });
        }

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            // #4606: under UseTenantPartitionedEvents, mt_events is itself a partitioned
            // table (by tenant_id) and so is mt_streams. Declaring a parent-level FK from
            // mt_events → mt_streams makes Postgres auto-propagate a partition-targeting
            // FK (mt_events → mt_streams_<tenant>) into mt_events as an INHERITED constraint
            // the moment the first mt_streams_<tenant> partition is attached. The next
            // mt_events_<tenant> partition-attach then trips
            // `42P16: cannot drop inherited constraint` because Weasel's
            // additivelyMigrateTablesForNewPartitions treats the auto-propagated FK as an
            // "extra" (not in Marten's declared FK set) and tries to drop it, which
            // Postgres refuses on an inherited constraint. The downstream symptom is
            // 23514 on the first event append because the partition attach was silently
            // swallowed by the migration's catch block.
            //
            // Skipping the explicit FK trades database-level referential integrity from
            // mt_events to mt_streams for the per-tenant-partitioning combination
            // working at all. Marten's append path always inserts/updates the stream row
            // before persisting events, so application-level integrity is preserved;
            // external tooling that writes mt_events directly without ensuring the
            // stream exists in mt_streams is the only at-risk path, and that's already
            // outside Marten's contract. The non-partitioned and archived-partitioning
            // shapes keep their FK because they don't trigger the auto-propagation.
            var skipMtEventsFkForTenantPartitioning = events.UseTenantPartitionedEvents;

            if (events.UseArchivedStreamPartitioning)
            {
                if (!skipMtEventsFkForTenantPartitioning)
                {
                    ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id_is_archived")
                    {
                        ColumnNames = new[] { TenantIdColumn.Name, "stream_id", "is_archived" },
                        LinkedNames = new[] { TenantIdColumn.Name, "id", "is_archived" },
                        LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams")
                    });
                }

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { TenantIdColumn.Name, "stream_id", "version", "is_archived" }
                });
            }
            else
            {
                if (!skipMtEventsFkForTenantPartitioning)
                {
                    ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id")
                    {
                        ColumnNames = new[] { TenantIdColumn.Name, "stream_id" },
                        LinkedNames = new[] { TenantIdColumn.Name, "id" },
                        LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams")
                    });
                }

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { TenantIdColumn.Name, "stream_id", "version" }
                });
            }


        }
        else
        {
            if (events.UseArchivedStreamPartitioning)
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_is_archived")
                {
                    ColumnNames = new[] { "stream_id", "is_archived" },
                    LinkedNames = new[] { "id", "is_archived" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams"),
                    DeleteAction = Weasel.Core.CascadeAction.Cascade
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { "stream_id", "version", "is_archived" }
                });
            }
            else
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id")
                {
                    ColumnNames = new[] { "stream_id" },
                    LinkedNames = new[] { "id" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams"),
                    DeleteAction = Weasel.Core.CascadeAction.Cascade
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { "stream_id", "version" }
                });
            }
        }

        if (events.EnableUniqueIndexOnEventId)
        {
            Indexes.Add(new IndexDefinition("idx_mt_events_event_id")
            {
                IsUnique = true, Columns = ["id"]
            });
        }

        if (events.EnableEventTypeIndex)
        {
            Indexes.Add(new IndexDefinition("idx_mt_events_event_type_seq_id")
            {
                Columns = ["type", "seq_id"]
            });
        }

        var archiving = AddColumn<IsArchivedColumn>();
        if (events.UseArchivedStreamPartitioning)
        {
            archiving.PartitionByListValues().AddPartition("archived", true);
        }

        // #4596 Session 1: per-tenant partitioning of mt_events via the existing
        // ManagedListPartitions instance. The combination with archived-partitioning
        // is rejected at config time (StoreOptions.Validate) — sub-partitioning is a
        // future deliverable. Partition rows arrive on the fly as tenants join, via
        // the standard `additivelyMigrateTablesForNewPartitions` path that scans every
        // table whose Partitioning uses this manager.
        if (events.UseTenantPartitionedEvents)
        {
            // Auto-init in StoreOptions.Validate guarantees TenantPartitions is non-null
            // by the time the EventsTable is constructed for schema generation.
            var manager = events.Options.TenantPartitions!.Partitions;
            Partitioning = new ListPartitioning { Columns = [TenantIdColumn.Name] }
                .UsePartitionManager(manager);

            // #4753 (mirrors #4706 for DocumentTable): a Marten-managed LIST partitioning keeps its
            // PARTITION BY clause but is exempt from child-partition reconciliation in the generic
            // schema diff. The per-tenant partitions are created out-of-band by
            // AddMartenManagedTenantsAsync / AddTenantToShardAsync (the additive path). Without this,
            // re-applying an unchanged schema over existing data sees the live per-tenant partitions
            // as "unexpected" and destructively rebuilds mt_events (CREATE _temp / DROP CASCADE /
            // recreate parent / INSERT…SELECT), failing with 23514 because the rebuilt parent has no
            // partitions yet. Setting this makes Weasel's destructive Rebuild path unreachable.
            IgnorePartitionsInMigration = true;
        }
    }

    internal IList<IEventTableColumn> SelectColumns()
    {
        var columns = new List<IEventTableColumn>();
        columns.AddRange(Columns.OfType<IEventTableColumn>());

        var data = columns.OfType<EventJsonDataColumn>().Single();
        var typeName = columns.OfType<EventTypeColumn>().Single();
        var dotNetTypeName = columns.OfType<DotNetTypeColumn>().Single();
        // #4515: bdata (nullable bytea) for binary event payloads. Pinned at
        // position 3 in the SELECT projection so EventDocumentStorage.Resolve
        // can pick JSON-vs-binary deserialization from a stable ordinal
        // before the per-column metadata loop runs.
        var bdata = columns.OfType<EventBdataColumn>().Single();

        columns.Remove(data);
        columns.Insert(0, data);
        columns.Remove(typeName);
        columns.Insert(1, typeName);
        columns.Remove(dotNetTypeName);
        columns.Insert(2, dotNetTypeName);
        columns.Remove(bdata);
        columns.Insert(3, bdata);

        return columns;
    }

    private void AddIfActive(MetadataColumn column)
    {
        if (column.Enabled)
        {
            AddColumn(column);
        }
    }
}

#endregion
