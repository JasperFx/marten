using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Schema
{
    internal class EventProgressionTable: Table
    {
        public EventProgressionTable(string schemaName) : base(new DbObjectName(schemaName, "mt_event_progression"))
        {
            AddPrimaryKey(new TableColumn("name", "varchar"));
            AddColumn("last_seq_id", "bigint", "NULL");
            AddColumn("last_updated", "timestamp with time zone", "DEFAULT transaction_timestamp()")
                .CanAdd = true;
        }
    }
}
