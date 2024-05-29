#nullable enable
namespace Marten.Linq;

/// <summary>
/// Specifies the direction used to sort the result items in a query using an <see cref="T:Remotion.Linq.Clauses.OrderByClause" />.
/// </summary>
public enum OrderingDirection
{
    /// <summary>
    /// Sorts the items in an ascending way, from smallest to largest.
    /// </summary>
    Asc,
    /// <summary>
    /// Sorts the items in an descending way, from largest to smallest.
    /// </summary>
    Desc,
}
