#nullable enable
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Marten.Linq.Caching;

/// <summary>
///     Replaces each of the given "slot" sub-expressions (matched by reference) with a
///     <see cref="ConstantExpression" /> holding a supplied replacement value. Used to
///     build the sentinel-valued template expression that the plan cache compiles once
///     per shape (see <see cref="LinqPlanRecorder" />).
/// </summary>
internal sealed class SlotReplacingVisitor: ExpressionVisitor
{
    private readonly Dictionary<Expression, object?> _replacements;

    public SlotReplacingVisitor(IReadOnlyList<Expression> slots, IReadOnlyList<object?> replacementValues)
    {
        _replacements = new Dictionary<Expression, object?>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < slots.Count; i++)
        {
            _replacements[slots[i]] = replacementValues[i];
        }
    }

    public override Expression? Visit(Expression? node)
    {
        if (node != null && _replacements.TryGetValue(node, out var value))
        {
            return Expression.Constant(value, node.Type);
        }

        return base.Visit(node);
    }
}

