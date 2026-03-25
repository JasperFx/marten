using System;
using JasperFx.Events.Tags;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTagTable: Table
{
    public EventTagTable(EventGraph events, ITagTypeRegistration registration)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, $"mt_event_tag_{registration.TableSuffix}"))
    {
        var pgType = PostgresqlTypeFor(registration.SimpleType);
        var isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;

        // Composite primary key with value first for query performance
        AddColumn("value", pgType).NotNull().AsPrimaryKey();

        // Add tenant_id to PK for conjoined tenancy to enable tenant-scoped tag queries
        if (isConjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        if (events.UseArchivedStreamPartitioning)
        {
            // When mt_events is partitioned by is_archived, its PK includes is_archived.
            // The FK must reference all PK columns, so we need is_archived in this table too.
            AddColumn("seq_id", "bigint").NotNull().AsPrimaryKey();

            var archiving = AddColumn<IsArchivedColumn>();
            archiving.AsPrimaryKey();
            archiving.PartitionByListValues().AddPartition("archived", true);

            ForeignKeys.Add(new ForeignKey($"fkey_mt_event_tag_{registration.TableSuffix}_seq_id_is_archived")
            {
                ColumnNames = new[] { "seq_id", "is_archived" },
                LinkedNames = new[] { "seq_id", "is_archived" },
                LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events")
            });
        }
        else
        {
            AddColumn("seq_id", "bigint").NotNull().AsPrimaryKey()
                .ForeignKeyTo(new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events"), "seq_id");
        }

        PrimaryKeyName = $"pk_mt_event_tag_{registration.TableSuffix}";
    }

    private static string PostgresqlTypeFor(Type simpleType)
    {
        if (simpleType == typeof(string)) return "text";
        if (simpleType == typeof(Guid)) return "uuid";
        if (simpleType == typeof(int)) return "integer";
        if (simpleType == typeof(long)) return "bigint";
        if (simpleType == typeof(short)) return "smallint";

        throw new ArgumentOutOfRangeException(nameof(simpleType),
            $"Unsupported tag value type '{simpleType.Name}'. Supported types: string, Guid, int, long, short.");
    }
}
