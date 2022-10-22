using System;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration
{
    internal class SubQueryStatement : Statement
    {
        private readonly string _locatorForElements;
        private readonly string _sourceTable;

        public SubQueryStatement(string locatorForElements, IMartenSession session, Statement sourceStatement) : base(null)
        {
            _sourceTable = sourceStatement.FromObject ?? throw new ArgumentNullException(nameof(sourceStatement));
            if (sourceStatement.FromObject.IsEmpty())
            {
                throw new ArgumentOutOfRangeException(nameof(sourceStatement),
                    "The source statement should not contain any empty FromObject");
            }
            _locatorForElements = locatorForElements ?? throw new ArgumentNullException(nameof(locatorForElements));

            ConvertToCommonTableExpression(session);
            sourceStatement.InsertBefore(this);
        }

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);

            sql.Append("select ctid, ");
            sql.Append(_locatorForElements);
            sql.Append(" as data from ");

            sql.Append(_sourceTable);


            if (Where != null)
            {
                sql.Append(" as d WHERE ");
                Where.Apply(sql);

                endCommonTableExpression(sql);
            }
            else
            {
                endCommonTableExpression(sql, " as d");
            }
        }
    }
}
