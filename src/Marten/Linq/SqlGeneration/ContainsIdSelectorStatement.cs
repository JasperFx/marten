using System.Linq.Expressions;
using Marten.Internal;
using Weasel.Postgresql;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    /// <summary>
    /// Used as an intermediate CTE in sub-collection Contains(primitive) queries
    /// </summary>
    internal class ContainsIdSelectorStatement: Statement
    {
        private readonly string _from;
        private readonly CommandParameter _parameter;

        public ContainsIdSelectorStatement(FlattenerStatement parent, IMartenSession session, ConstantExpression constant) : base(null)
        {
            ConvertToCommonTableExpression(session);
            _from = parent.ExportName;
            parent.InsertAfter(this);

            _parameter = new CommandParameter(constant);
        }

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);

            sql.Append("select id from ");
            sql.Append(_from);
            sql.Append(" where data = ");
            _parameter.Apply(sql);

            endCommonTableExpression(sql);
        }
    }
}
