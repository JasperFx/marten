#nullable enable
using System.Reflection;

namespace Marten.Internal.Storage;

/// <summary>
///     Database-neutral view of a duplicated field that the closed-shape storage runtime
///     consumes off <see cref="IDocumentStorage"/> — exposes only the members the storage /
///     patch code needs (the resolved member chain, the column name, and the pre-rendered
///     update fragment). Keeps the Npgsql-typed <c>DbType</c>/<c>PgType</c> and the LINQ member
///     model on the concrete <c>Marten.Linq.Members.DuplicatedField</c> out of the storage
///     surface, so the storage contract can move to the shared Weasel.Storage package (#4821).
/// </summary>
public interface IDuplicatedField
{
    /// <summary>The resolved member chain the duplicated column is derived from.</summary>
    MemberInfo[] Members { get; }

    /// <summary>The duplicated column's name.</summary>
    string ColumnName { get; }

    /// <summary>The <c>"col" = ...</c> assignment fragment used when patch/update SQL refreshes the column.</summary>
    string UpdateSqlFragment();
}
