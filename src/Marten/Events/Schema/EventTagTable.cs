using System;
using JasperFx.Events.Tags;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTagTable: Table
{
    public EventTagTable(EventGraph events, TagTypeRegistration registration)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName, $"mt_event_tag_{registration.TableSuffix}"))
    {
        var pgType = PostgresqlTypeFor(registration.SimpleType);

        // Composite primary key with value first for query performance
        AddColumn("value", pgType).NotNull().AsPrimaryKey();
        AddColumn("seq_id", "bigint").NotNull().AsPrimaryKey()
            .ForeignKeyTo(new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events"), "seq_id");

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
