using System;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Internal.CodeGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTypeColumn: TableColumn, IEventTableColumn
{
    public EventTypeColumn(): base("type", "varchar(500)")
    {
        AllowNulls = false;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.SetParameterFromMember<IEvent>(index, x => x.EventTypeName);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
