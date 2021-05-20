using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration
{
    public class AnySelectClause : ISelectClause, IQueryHandler<bool>
    {
        private Statement _topStatement;

        public AnySelectClause(string from)
        {
            FromObject = @from;
        }

        public string FromObject { get; }

        public Type SelectedType => typeof(bool);

        public void WriteSelectClause(CommandBuilder sql)
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

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement,
            Statement currentStatement)
        {
            _topStatement = topStatement;
            return (IQueryHandler<T>) this;
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new NotSupportedException("QueryStatistics is not valid with Any()/AnyAsync() queries");
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _topStatement.Configure(builder);
        }

        public bool Handle(DbDataReader reader, IMartenSession session)
        {
            if (!reader.Read())
                return false;

            return !reader.IsDBNull(0) && reader.GetBoolean(0);
        }

        public async Task<bool> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var hasRow = await reader.ReadAsync(token);

            return hasRow && !await reader.IsDBNullAsync(0, token) &&
                   await reader.GetFieldValueAsync<bool>(0, token);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
