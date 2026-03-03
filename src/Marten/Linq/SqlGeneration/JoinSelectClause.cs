#nullable enable
using System;
using JasperFx.Core;
using Marten.Internal;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// ISelectClause that renders a JOIN between two CTEs with a jsonb_build_object projection.
/// Produces: SELECT projection as data FROM outer_cte [INNER|LEFT] JOIN inner_cte ON condition
/// </summary>
internal class JoinSelectClause<T>: ISelectClause where T : notnull
{
    private readonly ISqlFragment _projection;
    private readonly string _outerCteAlias;
    private readonly string _innerCteAlias;
    private readonly bool _isLeftJoin;
    private readonly string _outerKeyLocator;
    private readonly string _innerKeyLocator;

    public JoinSelectClause(
        ISqlFragment projection,
        string outerCteAlias,
        string innerCteAlias,
        bool isLeftJoin,
        string outerKeyLocator,
        string innerKeyLocator)
    {
        _projection = projection;
        _outerCteAlias = outerCteAlias;
        _innerCteAlias = innerCteAlias;
        _isLeftJoin = isLeftJoin;
        _outerKeyLocator = outerKeyLocator;
        _innerKeyLocator = innerKeyLocator;
    }

    public string FromObject => _outerCteAlias;

    public Type SelectedType => typeof(T);

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");
        _projection.Apply(sql);
        sql.Append(" as data from ");
        sql.Append(_outerCteAlias);
        sql.Append(_isLeftJoin ? " LEFT" : " INNER");
        sql.Append(" JOIN ");
        sql.Append(_innerCteAlias);
        sql.Append(" ON ");
        sql.Append(_outerKeyLocator);
        sql.Append(" = ");
        sql.Append(_innerKeyLocator);
    }

    public string[] SelectFields()
    {
        return new[] { "data" };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return new SerializationSelector<T>(session.Serializer);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        var selector = new SerializationSelector<T>(session.Serializer);
        return LinqQueryParser.BuildHandler<T, TResult>(selector, topStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }
}
