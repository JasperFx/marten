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

internal class SelectDataSelectClause<T>: ISelectClause, IScalarSelectClause, IModifyableFromObject where T : notnull
{
    public SelectDataSelectClause(string from, ISqlFragment selector)
    {
        FromObject = from;
        Selector = selector;
    }

    public ISqlFragment Selector { get; }

    public Type SelectedType => typeof(T);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");

        if (Operator.IsNotEmpty())
        {
            sql.Append(Operator);
            sql.Append("(");
            Selector.Apply(sql);
            sql.Append(")");
        }
        else
        {
            Selector.Apply(sql);
        }

        sql.Append(" as data from ");

        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        // Fix for GH-3337
        var builder = new CommandBuilder();
        Selector.Apply(builder);

        var fieldName = builder.Compile().CommandText;

        return new[] { fieldName };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return new SerializationSelector<T>(session.Serializer);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        var selector = new SerializationSelector<T>(session.Serializer);

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

    public string MemberName => "calculated";
    public void ApplyOperator(string op)
    {
        Operator = op;
    }

    public string? Operator { get; set; }

    public ISelectClause CloneToDouble()
    {
        return new SelectDataSelectClause<double>(FromObject, Selector);
    }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new SelectDataSelectClause<T>(tableName, Selector);
    }
}

