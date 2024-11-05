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

internal class DataSelectClause<T>: ISelectClause, IScalarSelectClause, IModifyableFromObject
{
    public DataSelectClause(string from)
    {
        FromObject = from;
    }

    public DataSelectClause(string from, string field)
    {
        FromObject = from;
        MemberName = field;
    }

    public string MemberName { get; set; } = "d.data";

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new DataSelectClause<T>(tableName, MemberName);
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        return new DataSelectClause<double>(FromObject, MemberName);
    }

    public Type SelectedType => typeof(T);

    public string FromObject { get; set; }

    public void Apply(IPostgresqlCommandBuilder sql)
    {
        if (MemberName.IsNotEmpty())
        {
            sql.Append("select ");
            sql.Append(MemberName);
            sql.Append(" as data from ");
        }

        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { MemberName };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        if (typeof(T) == typeof(TimeSpan)) return new TimeSpanSelector();

        return new SerializationSelector<T>(session.Serializer);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement)
    {
        var selector = typeof(T) == typeof(TimeSpan) ? (ISelector<T>)new TimeSpanSelector() : new SerializationSelector<T>(session.Serializer);

        return LinqQueryParser.BuildHandler<T, TResult>(selector, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}
