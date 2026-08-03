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
/// <para>
/// The payload column is aliased to <see cref="DataAlias"/>. This decorator rebuilds the
/// <c>select</c> list from <see cref="ISelectClause.SelectFields"/> rather than delegating to the
/// inner clause's own <c>Apply</c>, which means any aliasing that <c>Apply</c> would have done is
/// lost — and the JSON streaming reader looks the payload up by the name <c>data</c>. For
/// <c>DataSelectClause</c> the name happened to survive because its field is the literal
/// <c>d.data</c>; for a <c>Select()</c> projection (<c>SelectDataSelectClause</c>, whose field is a
/// <c>jsonb_build_object(...)</c> expression) it did not, and the read failed with
/// <c>Field not found in row: data</c> (#5158). Aliasing explicitly makes the name intentional for
/// every inner clause instead of incidental for one.
/// </para>
/// </summary>
internal static class VersionSelectClause
{
    /// <summary>
    /// Result-set alias under which the piggy-backed <c>mt_version</c> value is returned.
    /// </summary>
    public const string VersionAlias = "mt_etag_version";

    /// <summary>
    /// Result-set alias under which the document payload is returned, matching the column name the
    /// JSON streaming reader looks for.
    /// </summary>
    public const string DataAlias = "data";
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

    /// <summary>
    /// The inner clause's fields with the payload explicitly aliased to <c>data</c> — see the
    /// remarks on <see cref="VersionSelectClause"/> for why the alias cannot be left implicit.
    /// <c>DocumentTable.SelectColumns</c> guarantees the payload is the first selected field
    /// ("the order of the selection is data, id, everything else"), so the remaining fields —
    /// e.g. the <c>mt_version</c> a revisioned document's storage already selects — pass through
    /// untouched and keep their own names.
    /// </summary>
    private string[] innerFields()
    {
        var fields = Inner.SelectFields().ToArray();
        fields[0] = $"{fields[0]} as {VersionSelectClause.DataAlias}";
        return fields;
    }

    public void Apply(ICommandBuilder sql)
    {
        sql.Append("select ");
        sql.Append(innerFields().Join(", "));
        sql.Append(", ");
        sql.Append(VersionColumn);
        sql.Append(" from ");
        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return innerFields().Concat(new[] { VersionColumn }).ToArray();
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
