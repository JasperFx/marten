using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.V4Internals.Linq
{
    public interface IScalarSelectClause
    {
        void ApplyOperator(string op);
    }

    public class ScalarSelectClause<T> : ISelectClause, ISelector<T>, IScalarSelectClause
    {
        private static readonly string NullResultMessage = $"The cast to value type '{typeof(T).FullNameInCode()}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";
        private string _locator;


        public ScalarSelectClause(IField field, string from)
        {
            FromObject = from;

            _locator = field.TypedLocator;
        }

        public string FromObject { get; }
        public void WriteSelectClause(CommandBuilder sql, bool withStatistics)
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

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement)
        {
            var selector = (ISelector<T>)BuildSelector(session);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        public T Resolve(DbDataReader reader)
        {
            try
            {
                return reader.GetFieldValue<T>(0);
            }
            catch (InvalidCastException e)
            {
                throw new InvalidOperationException(NullResultMessage, e);
            }
        }

        public Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            return reader.GetFieldValueAsync<T>(0, token);
        }

        public void ApplyOperator(string op)
        {
            _locator = $"{op}({_locator})";
        }
    }
}
