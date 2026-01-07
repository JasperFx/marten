using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Marten.Linq.Parsing;

public sealed class PatchingMemberFinder : MemberFinder
{
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "get_Item" && IsDictionary(node.Object?.Type))
        {
            var keyValue = EvaluateExpression(node.Arguments[0]);
            Members.Insert(0, new IndexerKeyInfo(keyValue?.ToString()!));

            if (node.Object != null)
                Visit(node.Object);

            return node;
        }

        return base.VisitMethodCall(node);
    }
    
    private static bool IsDictionary(Type? type)
    {
        if (type == null) return false;
    
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return true;
    
        return type.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }

    private static object? EvaluateExpression(Expression expression)
        => expression switch
        {
            ConstantExpression constant => constant.Value,
            MemberExpression member => EvaluateMemberExpression(member),
            UnaryExpression { NodeType: ExpressionType.Convert } unary => EvaluateExpression(unary.Operand),
            _ => CompileAndInvoke(expression)
        };

    private static object? EvaluateMemberExpression(MemberExpression member)
    {
        switch (member.Expression)
        {
            case ConstantExpression constant:
                return member.Member switch
                {
                    FieldInfo field => field.GetValue(constant.Value),
                    PropertyInfo prop => prop.GetValue(constant.Value),
                    _ => CompileAndInvoke(member)
                };

            case MemberExpression parentMember:
            {
                var parentValue = EvaluateMemberExpression(parentMember);
                return member.Member switch
                {
                    FieldInfo field => field.GetValue(parentValue),
                    PropertyInfo prop => prop.GetValue(parentValue),
                    _ => CompileAndInvoke(member)
                };
            }

            default:
                return CompileAndInvoke(member);
        }
    }

    private static object CompileAndInvoke(Expression expression)
    {
        var converted = Expression.Convert(expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(converted);
        return lambda.Compile()();
    }
}
