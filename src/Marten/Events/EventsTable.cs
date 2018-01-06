using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
    // SAMPLE: EventsTable
    public class EventsTable : Table
    {
        public EventsTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_events"))
        {
            var stringIdType = events.GetStreamIdType();

            AddPrimaryKey(new TableColumn("seq_id", "bigint"));
            AddColumn("id", "uuid", "NOT NULL");
            AddColumn("stream_id", stringIdType, $"REFERENCES {events.DatabaseSchemaName}.mt_streams ON DELETE CASCADE");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("data", "jsonb", "NOT NULL");
            AddColumn("type", "varchar(100)", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            AddColumn<TenantIdColumn>();
            AddColumn(new DotNetTypeColumn {Directive = "NULL"});

            Constraints.Add("CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)");
            Constraints.Add("CONSTRAINT pk_mt_events_id_unique UNIQUE(id)");
        }
    }
    // ENDSAMPLE
}