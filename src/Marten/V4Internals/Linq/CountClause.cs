using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Util;

namespace Marten.V4Internals.Linq
{
    public class CountClause<T> : ISelectClause, IQueryHandler<T>
    {
        private Statement _topStatement;

        public CountClause(string from)
        {
            FromObject = from;
        }

        public string FromObject { get; }
        public void WriteSelectClause(CommandBuilder sql, bool withStatistics)
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

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement topStatement)
        {
            _topStatement = topStatement;
            return (IQueryHandler<TResult>) this;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _topStatement.Configure(builder, false);
        }

        public T Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats)
        {
            var hasNext = reader.Read();
            return hasNext && !reader.IsDBNull(0)
                ? reader.GetFieldValue<T>(0)
                : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token)
        {
            var hasNext = await reader.ReadAsync(token).ConfigureAwait(false);
            return hasNext && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                ? await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false)
                : default(T);
        }
    }
}
