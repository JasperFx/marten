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

internal class NewScalarSelectClause<T>: ISelectClause, ISelector<T>, IScalarSelectClause, ISelector<T?>, IModifyableFromObject
    where T : struct
{
    private static readonly string NullResultMessage =
        $"The cast to value type '{typeof(T).FullNameInCode()}' failed because the materialized value is null. Either the result type's generic parameter or the query must use a nullable type.";

    public NewScalarSelectClause(string locator, string from)
    {
        FromObject = from;
        MemberName = locator;
    }

    public NewScalarSelectClause(IQueryableMember member, string from)
    {
        FromObject = from;

        MemberName = member.TypedLocator;
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

    public Type SelectedType => typeof(T);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
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
        ISqlFragment currentStatement) where TResult : notnull
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

        return await ReadScalarAsync(reader, 0, token).ConfigureAwait(false);
    }

    T? ISelector<T?>.Resolve(DbDataReader reader)
    {
        try
        {
            return reader.IsDBNull(0) ? null : ReadScalar(reader, 0);
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
            return reader.IsDBNull(0) ? default : ReadScalar(reader, 0);
        }
        catch (InvalidCastException e)
        {
            throw new InvalidOperationException(NullResultMessage, e);
        }
    }

    public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)) return default;
        return await ReadScalarAsync(reader, 0, token).ConfigureAwait(false);
    }

    private static T ReadScalar(DbDataReader reader, int ordinal)
    {
        if (!typeof(T).IsEnum)
        {
            return reader.GetFieldValue<T>(ordinal);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            var s = reader.GetFieldValue<string>(ordinal);
            return Enum.Parse<T>(s, ignoreCase: true);
        }

        var i = reader.GetFieldValue<int>(0);
        return (T)Enum.ToObject(typeof(T), i);
    }

    private static async Task<T> ReadScalarAsync(DbDataReader reader, int ordinal, CancellationToken token)
    {
        if (!typeof(T).IsEnum)
        {
            return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
        }

        if (reader.GetFieldType(ordinal) == typeof(string))
        {
            var s = await reader.GetFieldValueAsync<string>(ordinal, token).ConfigureAwait(false);
            return Enum.Parse<T>(s, ignoreCase: true);
        }

        var i = await reader.GetFieldValueAsync<int>(0, token).ConfigureAwait(false);
        return (T)Enum.ToObject(typeof(T), i);
    }
}
