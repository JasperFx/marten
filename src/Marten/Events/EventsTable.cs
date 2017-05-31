using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
    public class EventsTable : Table
    {
        public EventsTable(string schemaName) : base(new DbObjectName(schemaName, "mt_events"))
        {
            AddPrimaryKey(new TableColumn("seq_id", "bigint"));
            AddColumn("id", "uuid", "NOT NULL");
            AddColumn("stream_id", "uuid", $"REFERENCES {schemaName}.mt_streams ON DELETE CASCADE");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("data", "jsonb", "NOT NULL");
            AddColumn("type", "varchar(100)", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            
            Constraints.Add("CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)");
            Constraints.Add("CONSTRAINT pk_mt_events_id_unique UNIQUE(id)");
        }
    }
}