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

    /// <summary>
    /// The payload column as the document storage selects it, used to find the field to alias rather than
    /// assuming its position. Matches <c>DocumentStorage</c>, which builds its select fields as
    /// <c>d.{column.Name}</c>, and <c>DataColumn</c>, whose name is the same <c>data</c> the streaming reader
    /// looks up.
    /// </summary>
    private static readonly string PayloadColumn = $"d.{VersionSelectClause.DataAlias}";

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
    /// <para>
    /// The payload is <b>not</b> reliably the first selected field, whatever the older comment in
    /// <c>DocumentTable.SelectColumns</c> says: that method puts the id first whenever the id is selected at
    /// all, and <c>IdColumn.ShouldSelect</c> is <c>storageStyle != QueryOnly</c>. So a query through any
    /// identity-tracking session selects <c>d.id, d.data, …</c>, aliasing field 0 put the alias on the id
    /// column, and the streaming reader — which looks the payload up by the name <c>data</c> — found the id
    /// and wrote the document's id as the entire response body. A 200 whose payload does not deserialize
    /// into the document type.
    /// </para>
    /// <para>
    /// Matching the payload column by name fixes that for every storage style. The positional fallback stays
    /// for an inner clause that <i>projects</i> rather than selects the column — <c>SelectDataSelectClause</c>'s
    /// <c>jsonb_build_object(...)</c> under a <c>Select()</c> (#5158) — where there is no column name to match
    /// and the projection is the only candidate anyway.
    /// </para>
    /// </summary>
    private string[] innerFields()
    {
        var fields = Inner.SelectFields().ToArray();

        var payload = Array.IndexOf(fields, PayloadColumn);
        if (payload < 0)
        {
            payload = 0;
        }

        fields[payload] = $"{fields[payload]} as {VersionSelectClause.DataAlias}";
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
