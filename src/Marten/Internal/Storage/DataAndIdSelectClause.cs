using System;
using System.Linq;
using JasperFx.Core;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

internal class DataAndIdSelectClause<T>: ISelectClause, IModifyableFromObject
{
    private readonly IDocumentStorage<T> _inner;

    public DataAndIdSelectClause(IDocumentStorage<T> inner)
    {
        _inner = inner;
        FromObject = inner.FromObject;
    }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append($"select {_inner.SelectFields().Concat(["d.id"]).Join(", ")} from ");
        builder.Append(FromObject);
        builder.Append(" as d");
    }

    public string FromObject { get; set; }
    public Type SelectedType => typeof(T);
    public string[] SelectFields() => ["d.data", "d.id"];

    public ISelector BuildSelector(IMartenSession session)
    {
        return _inner.BuildSelector(session);
    }

    public IQueryHandler<T1> BuildHandler<T1>(IMartenSession session, ISqlFragment topStatement, ISqlFragment currentStatement)
    {
        return _inner.BuildHandler<T1>(session, topStatement, currentStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return _inner.UseStatistics(statistics);
    }
}
