using Marten.Internal;
using Marten.Linq.Fields;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

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
            sql.Append("select ctid, data from ");
            sql.Append(FromObject);
            sql.Append(" as d");
            writeWhereClause(sql);
            endCommonTableExpression(sql);
        }
    }

    internal class WhereCtIdInSubQuery : ISqlFragment
    {
        private readonly string _tableName;

        internal WhereCtIdInSubQuery(string tableName, FlattenerStatement flattenerStatement)
        {
            _tableName = tableName;
            Flattener = flattenerStatement;
        }

        public FlattenerStatement Flattener { get; }

        public void Apply(CommandBuilder builder)
        {
            builder.Append("ctid in (select ctid from ");
            builder.Append(this._tableName);
            builder.Append(")");
        }

        public bool Contains(string sqlText) => false;
    }
}
