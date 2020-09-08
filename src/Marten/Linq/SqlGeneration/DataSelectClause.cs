using System;
using Baseline;
using Marten.Internal;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class DataSelectClause<T> : ISelectClause, IScalarSelectClause
    {
        public DataSelectClause(string from)
        {
            FromObject = from;
        }

        public DataSelectClause(string from, string field)
        {
            FromObject = from;
            FieldName = field;
        }

        public Type SelectedType => typeof(T);

        public string FieldName { get; set; } = "d.data";
        public ISelectClause CloneToOtherTable(string tableName)
        {
            return new DataSelectClause<T>(tableName, FieldName);
        }

        public string FromObject { get; }
        public void WriteSelectClause(CommandBuilder sql)
        {
            if (FieldName.IsNotEmpty())
            {
                sql.Append("select ");
                sql.Append(FieldName);
                sql.Append(" as data from ");
            }
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            return new string[] {FieldName};
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            return new SerializationSelector<T>(session.Serializer);
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement,
            Statement currentStatement)
        {
            var selector = new SerializationSelector<T>(session.Serializer);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            return new StatsSelectClause<T>(this, statistics);
        }

        public void ApplyOperator(string op)
        {
            FieldName = $"{op}({FieldName})";
        }

        public ISelectClause CloneToDouble()
        {
            return new DataSelectClause<double>(FromObject, FieldName);
        }
    }
}
