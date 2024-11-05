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

public interface ICountClause: ISelectClause
{
    public void OverrideFromObject(Statement parent);
}

public class CountClause<T>: IQueryHandler<T>, ICountClause
{
    private ISqlFragment _topStatement;

    public CountClause(string from)
    {
        FromObject = from;
    }

    public void OverrideFromObject(Statement parent)
    {
        FromObject = parent.ExportName;
    }

    public void ConfigureCommand(IPostgresqlCommandBuilder builder, IMartenSession session)
    {
        _topStatement.Apply(builder);
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

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(T);

    public string FromObject { get; set; }

    public void Apply(IPostgresqlCommandBuilder sql)
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

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
    {
        _topStatement = topStatement;
        return (IQueryHandler<TResult>)this;
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        throw new NotSupportedException("QueryStatistics are not valid with a Count()/CountAsync() query");
    }

    public override string ToString()
    {
        return $"Count(*) from {FromObject}";
    }
}
