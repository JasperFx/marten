#nullable enable
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public class AnySelectClause: ISelectClause, IQueryHandler<bool>
{
    private ISqlFragment _topStatement;

    public AnySelectClause(string from)
    {
        FromObject = from;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        _topStatement.Apply(builder);
    }

    public bool Handle(DbDataReader reader, IMartenSession session)
    {
        if (!reader.Read())
        {
            return false;
        }

        return !reader.IsDBNull(0) && reader.GetBoolean(0);
    }

    public async Task<bool> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);

        return hasRow && !await reader.IsDBNullAsync(0, token).ConfigureAwait(false) &&
               await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }

    public string FromObject { get; }

    public Type SelectedType => typeof(bool);

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select TRUE as result");
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

    public IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where T: notnull
    {
        _topStatement = topStatement;
        return (IQueryHandler<T>)this;
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException("QueryStatistics is not valid with Any()/AnyAsync() queries");
    }
}
