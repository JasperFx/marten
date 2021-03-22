using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.Schema
{
    internal class DeadLetterEventsTable: Table
    {
        public DeadLetterEventsTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_dead_letter_events"))
        {
            AddPrimaryKey(new TableColumn("id", "uuid"));
            AddColumn("seq_id", "bigint");
            AddColumn("projection_name", "varchar");
            AddColumn("shard_name", "varchar");
            AddColumn(new EventTableColumn("timestamp", x => x.Timestamp)
            {
                Directive = "default (now()) NOT NULL", Type = "timestamptz"
            });
            AddColumn("exception_message", "varchar");
        }
    }
}
