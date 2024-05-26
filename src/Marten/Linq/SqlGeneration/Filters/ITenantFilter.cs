#nullable enable
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
///     Marker interface to help Marten track whether or not a Linq
///     query has some kind of tenant-aware filtering
/// </summary>
public interface ITenantFilter : ISqlFragment
{
}
