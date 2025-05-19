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
        var visitor = new MemberFinder();

        visitor.Visit(expression);

        return visitor.Members.ToArray();
    }

    public static MemberInfo[] Determine(Expression expression, string invalidMessage)
    {
        var visitor = new MemberFinder();

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
}
