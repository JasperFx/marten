#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
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
internal class JoinSelectClause<T>: ISelectClause, IScalarSelectClause where T : notnull
{
    private readonly ISqlFragment _projection;
    private readonly string _outerCteAlias;
    private readonly string _innerCteAlias;
    private readonly bool _isLeftJoin;
    private readonly string _outerKeyLocator;
    private readonly string _innerKeyLocator;

    // Set to "DISTINCT" when GroupJoin(...).SelectMany(...).Distinct() is used so the join
    // projection is rendered as DISTINCT(<projection>) — Postgres reads DISTINCT(x) as DISTINCT x.
    private string? _operator;

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
        if (_operator != null)
        {
            sql.Append(_operator);
            sql.Append("(");
        }

        _projection.Apply(sql);

        if (_operator != null)
        {
            sql.Append(")");
        }

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
        return new JoinDataSelector(session.Serializer);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        var selector = new JoinDataSelector(session.Serializer);
        return LinqQueryParser.BuildHandler<T, TResult>(selector, topStatement);
    }

    // The "data" column is null only for a bare scalar projection whose value is null
    // (e.g. a left join projecting an inner member of an unmatched outer row). Object
    // projections always render a non-null jsonb object. Return default for a null column
    // -- matching Marten's scalar-select convention -- instead of letting the serializer
    // throw "Column 'data' is null".
    private sealed class JoinDataSelector: ISelector<T>
    {
        private readonly ISerializer _serializer;

        public JoinDataSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Resolve(DbDataReader reader)
        {
            return reader.IsDBNull(0) ? default! : _serializer.FromJson<T>(reader, 0);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
            {
                return default!;
            }

            return await _serializer.FromJsonAsync<T>(reader, 0, token).ConfigureAwait(false);
        }
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    // IScalarSelectClause — implemented so GroupJoin(...).SelectMany(...).Distinct()[.Count()]
    // reuses the standard distinct machinery. Only DISTINCT is meaningful over a join projection;
    // the scalar aggregate operators (MIN/MAX/SUM/AVG) and table cloning are not applicable here.
    public string MemberName => "data";

    public void ApplyOperator(string op)
    {
        if (op != "DISTINCT")
        {
            throw new NotSupportedException(
                $"Marten does not support the '{op}' operator over a GroupJoin/SelectMany projection. Only Distinct() is supported.");
        }

        _operator = op;
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException(
            "Average aggregation is not supported over a GroupJoin/SelectMany projection.");
    }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        throw new NotSupportedException(
            "A GroupJoin/SelectMany projection cannot be cloned to another table.");
    }
}
