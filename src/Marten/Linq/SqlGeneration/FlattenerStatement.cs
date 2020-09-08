using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class FlattenerStatement : Statement
    {
        private readonly ArrayField _field;
        private readonly string _sourceTable;

        public FlattenerStatement(ArrayField field, IMartenSession session, Statement parentStatement) : base(null)
        {
            _sourceTable = parentStatement.FromObject;
            _field = field;

            ConvertToCommonTableExpression(session);
            parentStatement.InsertBefore(this);
        }

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);

            sql.Append("select id, ");
            sql.Append(_field.LocatorForFlattenedElements);
            sql.Append(" as data from ");

            sql.Append(_sourceTable);

            endCommonTableExpression(sql, " as d");
        }
    }
}
