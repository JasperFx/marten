using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class IdSelectorStatement : Statement
    {
        public IdSelectorStatement(IMartenSession session, IFieldMapping fields, Statement parent) : base(fields)
        {
            parent.InsertAfter(this);

            ConvertToCommonTableExpression(session);
        }

        protected override bool IsSubQuery => true;

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);
            sql.Append("select id from ");
            sql.Append(Previous.ExportName);
            sql.Append(" as d");
            writeWhereClause(sql);
            endCommonTableExpression(sql);
        }
    }
}
