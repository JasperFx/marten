using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public class CountClause<T> : ISelectClause, IQueryHandler<T>
    {
        private Statement _topStatement;

        public CountClause(string from)
        {
            FromObject = from;
        }

        public Type SelectedType => typeof(T);

        public string FromObject { get; }
        public void WriteSelectClause(CommandBuilder sql)
        {
            sql.Append("select count(*) as number");
            sql.Append(" from ");
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            throw new NotSupportedException();
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement topStatement,
            Statement currentStatement)
        {
            _topStatement = topStatement;
            return (IQueryHandler<TResult>) this;
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new NotSupportedException("QueryStatistics are not valid with a Count()/CountAsync() query");
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _topStatement.Configure(builder);
        }

        public T Handle(DbDataReader reader, IMartenSession session)
        {
            var hasNext = reader.Read();
            return hasNext && !reader.IsDBNull(0)
                ? reader.GetFieldValue<T>(0)
                : default;
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var hasNext = await reader.ReadAsync(token).ConfigureAwait(false);
            return hasNext && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false)
                : default;
        }
    }
}
