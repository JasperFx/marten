using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
    public class StreamsTable : Table
    {
        public StreamsTable(string schemaName) : base(new DbObjectName(schemaName, "mt_streams"))
        {
            AddPrimaryKey(new TableColumn("id", "uuid"));
            AddColumn("type", "varchar", "NULL");
            AddColumn("version", "integer", "NOT NULL");
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
            AddColumn("snapshot", "jsonb");
            AddColumn("snapshot_version", "integer");
        }
    }
}