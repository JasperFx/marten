using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Util;

namespace Marten.Internal.Linq.Includes
{
    public class IncludeIdentitySelectorStatement : Statement, ISelectClause
    {
        private readonly IList<IIncludePlan> _includes;
        private Statement _innerEnd;
        private Statement _clonedEnd;

        public IncludeIdentitySelectorStatement(Statement original, IList<IIncludePlan> includes,
            IMartenSession session) : base(null, null)
        {
            ExportName = session.NextTempTableName();

            _includes = includes;

            _innerEnd = original.Current();
            FromObject = _innerEnd.SelectClause.FromObject;

            _clonedEnd = _innerEnd.UseAsEndOfTempTableAndClone(this);
            _clonedEnd.SingleValue = _innerEnd.SingleValue;

            Inner = original;

            Statement current = this;
            foreach (var include in includes)
            {
                var includeStatement = include.BuildStatement(ExportName);
                current.Next = includeStatement;
                current = includeStatement;
            }

            current.Next = _clonedEnd;
        }



        protected override void compileStructure(MartenExpressionParser parser)
        {
            Inner.CompileStructure(parser);
        }

        public Type SelectedType => typeof(void);

        public Statement Inner { get; }

        protected override void configure(CommandBuilder sql)
        {
            sql.Append("create temp table ");
            sql.Append(ExportName);
            sql.Append(" as (\n");
            Inner.Configure(sql);
            sql.Append("\n);");
        }

        public string FromObject { get; }
        public bool IncludeDataInTempTable { get; set; }

        public void WriteSelectClause(CommandBuilder sql)
        {
            if (IncludeDataInTempTable)
            {
                // Basically if the data for the Include is coming from a
                // SelectMany() clause
                sql.Append("select data, ");
            }
            else
            {
                sql.Append("select id, ");
            }


            sql.Append(_includes.Select(x => x.TempSelector).Join(", "));
            sql.Append(" from ");
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            return _includes.Select(x => x.TempSelector).ToArray();
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            throw new System.NotSupportedException();
        }

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement,
            Statement currentStatement)
        {
            // It's wrapped in LinqHandlerBuilder
            return _clonedEnd.SelectClause.BuildHandler<T>(session, topStatement, currentStatement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new System.NotSupportedException();
        }
    }
}
