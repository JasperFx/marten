using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTypeColumn: TableColumn, IEventTableColumn
{
    public EventTypeColumn(): base("type", "varchar(500)")
    {
        AllowNulls = false;
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
