using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Internal.Linq
{
    public class ScalarSelectClause<T> : ISelectClause, ISelector<T>, IScalarSelectClause, ISelector<Nullable<T>> where T : struct
    {
        private static readonly string NullResultMessage = $"The cast to value type '{typeof(T).FullNameInCode()}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";
        private string _locator;

        public ScalarSelectClause(string locator, string from)
        {
            FromObject = from;
            _locator = locator;
        }

        public ScalarSelectClause(IField field, string from)
        {
            FromObject = from;

            _locator = field.TypedLocator;
        }

        public string FieldName => _locator;
        public ISelectClause CloneToOtherTable(string tableName)
        {
            return new ScalarSelectClause<T>(_locator, tableName);
        }

        public Type SelectedType => typeof(T);

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
            var selector = (ISelector<T>)BuildSelector(session);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            return new StatsSelectClause<T>(this, statistics);
        }

        public T Resolve(DbDataReader reader)
        {
            try
            {
                if (reader.IsDBNull(0))
                {
                    return default(T);
                }

                return reader.GetFieldValue<T>(0);
            }
            catch (InvalidCastException e)
            {
                throw new InvalidOperationException(NullResultMessage, e);
            }
        }

        async Task<T?> ISelector<T?>.ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                return null;
            }

            return await reader.GetFieldValueAsync<T>(0, token);
        }

        T? ISelector<T?>.Resolve(DbDataReader reader)
        {
            try
            {
                if (reader.IsDBNull(0))
                {
                    return null;
                }

                return reader.GetFieldValue<T>(0);
            }
            catch (InvalidCastException e)
            {
                throw new InvalidOperationException(NullResultMessage, e);
            }
        }

        public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                return default(T);
            }

            return await reader.GetFieldValueAsync<T>(0, token);
        }

        public void ApplyOperator(string op)
        {
            _locator = $"{op}({_locator})";
        }

        public ISelectClause CloneToDouble()
        {
            return new ScalarSelectClause<double>(_locator,FromObject);
        }
    }
}
