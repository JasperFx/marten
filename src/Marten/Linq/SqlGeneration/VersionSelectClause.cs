#nullable enable
using System;
using System.Linq;
using JasperFx.Core;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
/// Decorates an inner <see cref="ISelectClause"/> so the streaming query also
/// selects the document's <c>mt_version</c> column alongside its payload, letting the
/// single-document JSON streaming path read the version in the SAME round trip
/// (used by the ASP.NET Core <c>StreamOne</c> ETag support). Mirrors
/// <see cref="StatsSelectClause{T}"/>, which appends <c>count(*) OVER()</c> the same way.
/// <para>
/// The version is aliased to <see cref="VersionAlias"/> (not the bare <c>mt_version</c>)
/// so it never collides with an <c>mt_version</c> column the inner clause may already
/// select (e.g. under optimistic concurrency).
/// </para>
/// </summary>
internal static class VersionSelectClause
{
    /// <summary>
    /// Result-set alias under which the piggy-backed <c>mt_version</c> value is returned.
    /// </summary>
    public const string VersionAlias = "mt_etag_version";
}

internal class VersionSelectClause<T>: ISelectClause, IModifyableFromObject where T : notnull
{
    private static readonly string VersionColumn =
        $"d.{SchemaConstants.VersionColumn} as {VersionSelectClause.VersionAlias}";

    public VersionSelectClause(ISelectClause inner)
    {
        Inner = inner;
        FromObject = Inner.FromObject;
    }

    public ISelectClause Inner { get; }

    public Type SelectedType => Inner.SelectedType;

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(Inner.SelectFields().Join(", "));
        sql.Append(", ");
        sql.Append(VersionColumn);
        sql.Append(" from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return Inner.SelectFields().Concat(new[] { VersionColumn }).ToArray();
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
