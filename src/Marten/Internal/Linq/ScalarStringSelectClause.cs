using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Internal.Linq
{
    public class ScalarStringSelectClause: ISelectClause, IScalarSelectClause, ISelector<string>
    {
        private string _locator;

        public ScalarStringSelectClause(string field, string from)
        {
            FromObject = from;
            _locator = field;
        }

        public ScalarStringSelectClause(IField field, string from)
        {
            FromObject = from;

            _locator = field.TypedLocator;
        }

        public string FieldName => _locator;

        public Type SelectedType => typeof(string);

        public string FromObject { get; }

        public void WriteSelectClause(CommandBuilder sql)
        {
            sql.Append("select ");
            sql.Append(_locator);
            sql.Append(" from ");
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            return new string[]{_locator};
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            return this;
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement,
            Statement currentStatement)
        {
            return LinqHandlerBuilder.BuildHandler<string, TResult>(this, statement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            return new StatsSelectClause<string>(this, statistics);
        }

        public string Resolve(DbDataReader reader)
        {
            if (reader.IsDBNull(0))
            {
                return null;
            }

            return reader.GetFieldValue<string>(0);
        }

        public async Task<string> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                return null;
            }

            return await reader.GetFieldValueAsync<string>(0, token);
        }



        public void ApplyOperator(string op)
        {
            _locator = $"{op}({_locator})";
        }

        public ISelectClause CloneToDouble()
        {
            throw new NotSupportedException();
        }
    }
}
