using JasperFx.CodeGeneration;

namespace Marten.Events.Schema;

internal class SequenceColumn: EventTableColumn
{
    public SequenceColumn() : base("seq_id", x => x.Sequence)
    {
        AllowNulls = false;
    }

    public override string ValueSql(EventGraph graph, AppendMode mode)
    {
        return mode == AppendMode.Full ? base.ValueSql(graph, mode) : $"nextval('{graph.DatabaseSchemaName}.mt_events_sequence')";
    }


    public override void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode mode)
    {
        if (mode == AppendMode.Full)
        {
            base.GenerateAppendCode(method, graph, index, mode);
        }
    }
}
