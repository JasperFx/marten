using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
    public class EventProgressionTable: Table
    {
        public EventProgressionTable(string schemaName) : base(new DbObjectName(schemaName, "mt_event_progression"))
        {
            AddPrimaryKey(new TableColumn("name", "varchar"));
            AddColumn("last_seq_id", "bigint", "NULL");
        }
    }
}
