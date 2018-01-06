using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
    public class StreamsTable : Table
    {
        public StreamsTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_streams"))
        {
            AddPrimaryKey(events.StreamIdentity == StreamIdentity.AsGuid
                ? new TableColumn("id", "uuid")
                : new TableColumn("id", "varchar"));


            AddColumn("type", "varchar", "NULL");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            AddColumn("snapshot", "jsonb");
            AddColumn("snapshot_version", "integer");
            AddColumn("created", "timestamptz", "default (now()) NOT NULL").CanAdd = true;
            AddColumn<TenantIdColumn>();
        }
    }
}