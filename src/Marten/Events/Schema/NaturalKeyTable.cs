using JasperFx.Events;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class NaturalKeyTable: Table
{
    public NaturalKeyTable(EventGraph events, NaturalKeyDefinition naturalKey)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName,
            $"mt_natural_key_{naturalKey.AggregateType.Name.ToLowerInvariant()}"))
    {
        // Determine the column type for the natural key value
        var columnType = naturalKey.InnerType == typeof(int) ? "integer"
            : naturalKey.InnerType == typeof(long) ? "bigint"
            : "varchar(200)";

        AddColumn("natural_key_value", columnType).AsPrimaryKey().NotNull();

        // Stream identity column - matches mt_streams.id type
        var streamCol = events.StreamIdentity == StreamIdentity.AsGuid ? "stream_id" : "stream_key";
        var streamColType = events.StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar(250)";

        AddColumn(streamCol, streamColType).NotNull();

        // FK to mt_streams with CASCADE delete
        ForeignKeys.Add(new ForeignKey($"fk_{Identifier.Name}_stream")
        {
            ColumnNames = new[] { streamCol },
            LinkedNames = new[] { "id" },
            LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, StreamsTable.TableName),
            OnDelete = CascadeAction.Cascade
        });

        // Tenancy support
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>();
        }

        // Archive support
        var archiving = AddColumn<IsArchivedColumn>();
        if (events.UseArchivedStreamPartitioning)
        {
            archiving.PartitionByListValues().AddPartition("archived", true);
        }

        // Index on stream id/key for reverse lookups
        Indexes.Add(new IndexDefinition($"idx_{Identifier.Name}_{streamCol}")
        {
            IsUnique = false,
            Columns = new[] { streamCol }
        });
    }
}
