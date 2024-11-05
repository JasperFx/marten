#nullable enable
using System;
using System.Linq;
using JasperFx.Core;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal interface IStatsSelectClause
{
    ISelectClause Inner { get; }
}

internal class StatsSelectClause<T>: ISelectClause, IModifyableFromObject, IStatsSelectClause
{
    private QueryStatistics _statistics;

    public StatsSelectClause(ISelectClause inner, QueryStatistics statistics)
    {
        Inner = inner;
        _statistics = statistics;
        FromObject = Inner.FromObject;
    }

    public ISelectClause Inner { get; }

    public Type SelectedType => Inner.SelectedType;

    public string FromObject { get; set; }

    public void Apply(IPostgresqlCommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(Inner.SelectFields().Join(", "));
        sql.Append(", ");
        sql.Append(LinqConstants.StatsColumn);
        sql.Append(" from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return Inner.SelectFields().Concat(new[] { LinqConstants.StatsColumn }).ToArray();
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return Inner.BuildSelector(session);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
    {
        var selector = (ISelector<T>)Inner.BuildSelector(session);

        var handler =
            new ListWithStatsQueryHandler<T>(Inner.SelectFields().Length, topStatement, selector, _statistics);

        if (handler is IQueryHandler<TResult> h)
        {
            return h;
        }

        throw new NotSupportedException("QueryStatistics queries are only supported for enumerable results");
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        _statistics = statistics;
        return this;
    }
}
