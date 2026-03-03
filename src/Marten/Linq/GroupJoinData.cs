#nullable enable
using System;
using System.Linq.Expressions;

namespace Marten.Linq;

/// <summary>
/// Holds parsed GroupJoin expression components for LINQ GroupJoin translation to SQL JOIN.
/// </summary>
public class GroupJoinData
{
    /// <summary>
    /// The inner IQueryable source expression (e.g., session.Query&lt;Order&gt;())
    /// </summary>
    public Expression InnerSourceExpression { get; set; } = null!;

    /// <summary>
    /// The outer key selector lambda (e.g., c => c.Id)
    /// </summary>
    public LambdaExpression OuterKeySelector { get; set; } = null!;

    /// <summary>
    /// The inner key selector lambda (e.g., o => o.CustomerId)
    /// </summary>
    public LambdaExpression InnerKeySelector { get; set; } = null!;

    /// <summary>
    /// The GroupJoin result selector lambda (e.g., (c, orders) => new { c, orders })
    /// </summary>
    public LambdaExpression ResultSelector { get; set; } = null!;

    /// <summary>
    /// The document type of the inner collection (e.g., typeof(Order))
    /// </summary>
    public Type InnerElementType { get; set; } = null!;

    /// <summary>
    /// True when DefaultIfEmpty() is detected, producing a LEFT JOIN instead of INNER JOIN
    /// </summary>
    public bool IsLeftJoin { get; set; }

    /// <summary>
    /// The flattened result selector from the SelectMany that follows GroupJoin
    /// (e.g., (c, o) => new { c.Name, o.Amount })
    /// </summary>
    public LambdaExpression? FlattenedResultSelector { get; set; }
}
