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
/// Marker interface for select clauses whose result is produced by executing .NET code
/// against the fully deserialized source document rather than by a SQL expression the
/// database computes directly (GH-5011). The "data" column for these clauses holds the
/// *source* document's raw JSON, not the shape of <c>TResult</c>, so raw JSON streaming
/// (StreamJsonArray/StreamOne/StreamMany/StreamJsonFirst/StreamJsonSingle) must refuse to
/// operate on them -- doing so would copy the wrong bytes to the caller.
/// </summary>
internal interface IClientSideProjectionSelectClause
{
}

/// <summary>
/// Select clause used as a fallback when a Select() projection is not a "simple" flat
/// member-access transform (GH-5011) -- i.e. it contains method calls, arithmetic,
/// casts, or conditional expressions that can't be translated into a
/// jsonb_build_object() SQL expression. The full source document is selected as-is and
/// the original Select() lambda is compiled and applied against it on the client, which
/// is exactly how Marten always projected before the jsonb_build_object optimization
/// existed, so this is purely a fallback with no behavior change.
/// </summary>
internal class LambdaSelectClause<TSource, TResult>: ISelectClause, IScalarSelectClause,
    IModifyableFromObject, IDistinctOnSelectClause, IClientSideProjectionSelectClause
    where TSource : notnull
    where TResult : notnull
{
    private readonly Func<TSource, TResult> _transform;

    public LambdaSelectClause(string from, Func<TSource, TResult> transform)
    {
        FromObject = from;
        _transform = transform;
    }

    public string? DistinctOn { get; set; }

    public Type SelectedType => typeof(TResult);

    public string FromObject { get; set; }

    public string MemberName => "d.data";

    public string? Operator { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");

        if (DistinctOn.IsNotEmpty())
        {
            sql.Append("distinct on (");
            sql.Append(DistinctOn);
            sql.Append(") ");
        }

        if (Operator.IsNotEmpty())
        {
            sql.Append(Operator);
            sql.Append("(d.data)");
        }
        else
        {
            sql.Append("d.data");
        }

        sql.Append(" as data from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { "d.data" };
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        return new LambdaTransformSelector<TSource, TResult>(session.Serializer, _transform);
    }

    public IQueryHandler<TResult2> BuildHandler<TResult2>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult2 : notnull
    {
        var selector = new LambdaTransformSelector<TSource, TResult>(session.Serializer, _transform);

        return LinqQueryParser.BuildHandler<TResult, TResult2>(selector, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TResult>(this, statistics);
    }

    public override string ToString()
    {
        return $"Client-side Select() transform from {FromObject}";
    }

    public void ApplyOperator(string op)
    {
        Operator = op;
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException(
            "Aggregate operations (Sum/Min/Max/Average) are not supported against a Select() projection that requires client-side evaluation (GH-5011)");
    }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new LambdaSelectClause<TSource, TResult>(tableName, _transform) { DistinctOn = DistinctOn, Operator = Operator };
    }
}
