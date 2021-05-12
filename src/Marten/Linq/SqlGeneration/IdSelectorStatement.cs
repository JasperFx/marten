using Marten.Internal;
using Marten.Linq.Fields;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    internal class IdSelectorStatement : Statement
    {
        public IdSelectorStatement(IMartenSession session, IFieldMapping fields, Statement parent) : base(fields)
        {
            parent.InsertAfter(this);

            ConvertToCommonTableExpression(session);

            // Important when doing n-deep sub-collection querying
            // And you need to remember the original FromObject
            // of the original parent rather than looking at Previous.ExportName
            // in the configure() method
            FromObject = parent.ExportName;
        }

        protected override bool IsSubQuery => true;

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);
            sql.Append("select id, data from ");
            sql.Append(FromObject);
            sql.Append(" as d");
            writeWhereClause(sql);
            endCommonTableExpression(sql);
        }
    }
}
