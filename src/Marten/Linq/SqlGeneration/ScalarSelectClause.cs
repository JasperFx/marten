using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class ScalarSelectClause<T>: ISelectClause, ISelector<T>, IScalarSelectClause, ISelector<T?> where T : struct
{
    private static readonly string NullResultMessage =
        $"The cast to value type '{typeof(T).FullNameInCode()}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";

    public ScalarSelectClause(string locator, string from)
    {
        FromObject = from;
        MemberName = locator;
    }

    public ScalarSelectClause(IQueryableMember field, string from)
    {
        FromObject = from;

        MemberName = field.TypedLocator;
    }

    public string MemberName { get; private set; }

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new ScalarSelectClause<T>(MemberName, tableName);
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        return new ScalarSelectClause<double>(MemberName, FromObject);
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        return false;
    }

    public Type SelectedType => typeof(T);

    public string FromObject { get; }

    public void Apply(CommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(MemberName);
        sql.Append(" from ");
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
        ISqlFragment currentStatement)
    {
        var selector = (ISelector<T>)BuildSelector(session);

        return LinqQueryParser.BuildHandler<T, TResult>(selector, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    async Task<T?> ISelector<T?>.ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return null;
        }

        return await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
    }

    T? ISelector<T?>.Resolve(DbDataReader reader)
    {
        try
        {
            if (reader.IsDBNull(0))
            {
                return null;
            }

            return reader.GetFieldValue<T>(0);
        }
        catch (InvalidCastException e)
        {
            throw new InvalidOperationException(NullResultMessage, e);
        }
    }

    public T Resolve(DbDataReader reader)
    {
        try
        {
            if (reader.IsDBNull(0))
            {
                return default;
            }

            return reader.GetFieldValue<T>(0);
        }
        catch (InvalidCastException e)
        {
            throw new InvalidOperationException(NullResultMessage, e);
        }
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return default;
        }

        return await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
    }

    public override string ToString()
    {
        return $"Select {typeof(T).ShortNameInCode()} from {FromObject}";
    }
}
