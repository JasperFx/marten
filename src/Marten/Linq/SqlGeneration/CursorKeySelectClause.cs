#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Decorates an inner <see cref="ISelectClause"/> so a keyset ("cursor") page query also
/// selects the ORDER BY key column(s) alongside the document <c>data</c>, letting the
/// raw-JSON cursor-paging path read the next cursor's key values off the SAME
/// <see cref="System.Data.Common.DbDataReader"/> row instead of hydrating each document.
/// Mirrors <see cref="StatsSelectClause{T}"/> / <c>VersionSelectClause</c>, which append an
/// extra projected column the same way. The appended columns are pre-formatted
/// <c>&lt;locator&gt; as cursor_key_N</c> expressions supplied by the caller.
/// </summary>
internal class CursorKeySelectClause: ISelectClause, IModifyableFromObject
{
    private readonly IReadOnlyList<string> _keyColumns;

    public CursorKeySelectClause(ISelectClause inner, IReadOnlyList<string> keyColumns)
    {
        Inner = inner;
        _keyColumns = keyColumns;
        FromObject = Inner.FromObject;
    }

    public ISelectClause Inner { get; }

    public Type SelectedType => Inner.SelectedType;

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(Inner.SelectFields().Join(", "));
        foreach (var column in _keyColumns)
        {
            sql.Append(", ");
            sql.Append(column);
        }

        sql.Append(" from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return Inner.SelectFields().Concat(_keyColumns).ToArray();
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        return Inner.BuildSelector(session);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where TResult: notnull
    {
        return Inner.BuildHandler<TResult>(session, topStatement, currentStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return Inner.UseStatistics(statistics);
    }
}
