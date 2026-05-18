using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventJsonDataColumn: TableColumn, IEventTableColumn
{
    public EventJsonDataColumn(): base("data", "jsonb")
    {
        AllowNulls = false;
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
