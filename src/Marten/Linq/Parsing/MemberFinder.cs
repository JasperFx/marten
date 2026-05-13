#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.QueryHandlers;

namespace Marten.Linq.Parsing;

// TODO -- this needs to account for Methods too!. See https://github.com/JasperFx/marten/issues/2707
public class MemberFinder: ExpressionVisitor
{
    public readonly List<ExpressionType> InvalidNodeTypes = new();
    public readonly IList<MemberInfo> Members = new List<MemberInfo>();

    public bool FoundParameterAtFront { get; private set; }

    // 9.0 (#4390): one-deep per-thread pool for the LINQ-parse hot path. Determine() is
    // called once per LINQ member-chain visit during query parsing; pooling drops the
    // visitor allocation on the second-and-later LINQ call per thread. Subclasses
    // (PatchingMemberFinder) intentionally bypass this — the pool is type-specific.
    [System.ThreadStatic] private static MemberFinder? _pooled;

    internal static MemberFinder Rent()
    {
        var finder = _pooled;
        if (finder is null)
        {
            return new MemberFinder();
        }

        _pooled = null;
        finder.ResetState();
        return finder;
    }

    internal static void Return(MemberFinder finder)
    {
        // The pool only ever holds a single instance. Recursive Determine calls on the
        // same thread (rare but possible — a visitor's expression could indirectly call
        // back into Determine) get a fresh allocation on the inner call rather than
        // reusing the still-in-flight pooled visitor. The outer call's return wins the
        // pool slot. This is fine because the cost is bounded by recursion depth.
        finder.ResetState();
        _pooled = finder;
    }

    private void ResetState()
    {
        Members.Clear();
        InvalidNodeTypes.Clear();
        FoundParameterAtFront = false;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        Members.Insert(0, node.Member);

        try
        {
            return base.VisitMember(node);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return null;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        InvalidNodeTypes.Add(node.NodeType);
        return base.VisitBinary(node);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        FoundParameterAtFront = true;
        return base.VisitParameter(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Count" && node.Method.ReturnType == typeof(int))
        {
            Members.Insert(0, LinqConstants.ArrayLength);
        }

        // This is fugly!
        if (node.Method.Name.IsOneOf([
                nameof(String.ToLower), nameof(String.ToUpper), nameof(String.ToLowerInvariant),
                nameof(String.ToUpperInvariant)
            ]))
        {
            Members.Insert(0, node.Method);
        }

        return base.VisitMethodCall(node);
    }

    protected sealed override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.ArrayLength)
        {
            Members.Insert(0, LinqConstants.ArrayLength);
        }

        return base.VisitUnary(node);
    }

    public static MemberInfo[] Determine(Expression expression)
    {
        var visitor = Rent();
        try
        {
            visitor.Visit(expression);
            return visitor.Members.ToArray();
        }
        finally
        {
            Return(visitor);
        }
    }

    public static MemberInfo[] Determine(Expression expression, string invalidMessage)
    {
        var visitor = Rent();
        try
        {
            visitor.Visit(expression);

            if (!visitor.FoundParameterAtFront)
            {
                throw new BadLinqExpressionException($"{invalidMessage}: '{expression}'");
            }

            if (visitor.InvalidNodeTypes.Any())
            {
                throw new BadLinqExpressionException($"{invalidMessage}: '{expression}'");
            }

            return visitor.Members.ToArray();
        }
        finally
        {
            Return(visitor);
        }
    }
}
