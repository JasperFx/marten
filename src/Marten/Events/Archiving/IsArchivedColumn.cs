using LamarCodeGeneration;
using Marten.Events.Schema;
using Marten.Internal.CodeGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Archiving
{
    internal class IsArchivedColumn: TableColumn, IEventTableColumn
    {
        internal const string ColumnName = "is_archived";

        public IsArchivedColumn() : base(ColumnName, "bool")
        {
            DefaultExpression = "FALSE";
        }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.IsArchived);
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.IsArchived);
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            throw new System.NotSupportedException();
        }
    }
}
