using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public class ValueTypeSelectClause<TOuter, TInner>: ISelectClause, IScalarSelectClause, IModifyableFromObject,
    ISelector<TOuter>, ISelector<TOuter?> where TOuter : struct
{
    public ValueTypeSelectClause(string memberName, Func<TInner, TOuter> converter)
    {
        MemberName = memberName;
        Converter = converter;
    }

    public Func<TInner, TOuter> Converter { get; }

    public string MemberName { get; set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ValueTypeSelectClause<TOuter, TInner>(MemberName, Converter)
        {
            FromObject = tableName, MemberName = MemberName
        };
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(TOuter);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
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
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult: notnull
    {
        if (typeof(TResult).CanBeCastTo<IEnumerable<TOuter>>())
        {
            return (IQueryHandler<TResult>)new ListQueryHandler<TOuter>(statement, this);
        }

        return (IQueryHandler<TResult>)new ListQueryHandler<TOuter?>(statement, this);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TOuter>(this, statistics);
    }

    public TOuter Resolve(DbDataReader reader)
    {
        var inner = reader.GetFieldValue<TInner>(0);
        return Converter(inner);
    }

    async Task<TOuter?> ISelector<TOuter?>.ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return null;
        }

        return await ResolveAsync(reader, token).ConfigureAwait(false);
    }

    TOuter? ISelector<TOuter?>.Resolve(DbDataReader reader)
    {
        if (reader.IsDBNull(0))
        {
            return null;
        }

        return Resolve(reader);
    }

    public async Task<TOuter> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var inner = await reader.GetFieldValueAsync<TInner>(0, token).ConfigureAwait(false);
        return Converter(inner);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}

public class ClassValueTypeSelectClause<TOuter, TInner>: ISelectClause, IScalarSelectClause, IModifyableFromObject,
    ISelector<TOuter> where TOuter : notnull
{
    public ClassValueTypeSelectClause(string memberName, Func<TInner, TOuter> converter)
    {
        MemberName = memberName;
        Converter = converter;
    }

    public Func<TInner, TOuter> Converter { get; }

    public string MemberName { get; set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ClassValueTypeSelectClause<TOuter, TInner>(MemberName, Converter)
        {
            FromObject = tableName, MemberName = MemberName
        };
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(TOuter);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
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
        return [MemberName];
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult: notnull
    {
        if (typeof(TResult).CanBeCastTo<IEnumerable<TOuter>>())
        {
            return (IQueryHandler<TResult>)new ListQueryHandler<TOuter>(statement, this);
        }

        return (IQueryHandler<TResult>)new ListQueryHandler<TOuter?>(statement, this);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TOuter>(this, statistics);
    }

    public TOuter Resolve(DbDataReader reader)
    {
        if (reader.IsDBNull(0))
        {
            return default(TOuter);
        }

        var inner = reader.GetFieldValue<TInner>(0);
        return Converter(inner);
    }

    public async Task<TOuter> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return default(TOuter);
        }

        var inner = await reader.GetFieldValueAsync<TInner>(0, token).ConfigureAwait(false);
        return Converter(inner);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}
