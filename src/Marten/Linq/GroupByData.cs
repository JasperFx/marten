#nullable enable
using System.Linq.Expressions;

namespace Marten.Linq;

/// <summary>
/// Holds parsed GroupBy expression components for LINQ GroupBy translation to SQL GROUP BY.
/// </summary>
public class GroupByData
{
    /// <summary>
    /// The key selector lambda (e.g., x => x.Color)
    /// </summary>
    public LambdaExpression KeySelector { get; set; } = null!;
}
