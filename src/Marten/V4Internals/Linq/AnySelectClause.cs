using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Util;

namespace Marten.V4Internals.Linq
{
    public class AnySelectClause : ISelectClause, IQueryHandler<bool>
    {
        private Statement _topStatement;

        public AnySelectClause(string from)
        {
            FromObject = @from;
        }

        public string FromObject { get; }

        public void WriteSelectClause(CommandBuilder sql, bool withStatistics)
        {
            sql.Append("select (count(*) > 0) as result");
            sql.Append(" from ");
            sql.Append(FromObject);
            sql.Append(" as d");
        }

        public string[] SelectFields()
        {
            throw new System.NotSupportedException();
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement)
        {
            _topStatement = topStatement;
            return (IQueryHandler<T>) this;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _topStatement.Configure(builder, false);
        }

        public bool Handle(DbDataReader reader, IMartenSession session, QueryStatistics stats)
        {
            if (!reader.Read())
                return false;

            return !reader.IsDBNull(0) && reader.GetBoolean(0);
        }

        public async Task<bool> HandleAsync(DbDataReader reader, IMartenSession session, QueryStatistics stats, CancellationToken token)
        {
            var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);

            return hasRow && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false) &&
                   await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false);
        }
    }
}
