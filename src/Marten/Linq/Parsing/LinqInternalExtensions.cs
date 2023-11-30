using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using FastExpressionCompiler;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

internal static class LinqInternalExtensions
{
    // private static Type[] _valueTypes = new Type[]
    // {
    //     typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly)
    // };

    private static readonly List<Type> _valueTypes = new();

    static LinqInternalExtensions()
    {
        addValueType<Guid>();
        addValueType<DateTime>();
        addValueType<DateTimeOffset>();
        addValueType<DateOnly>();
        addValueType<TimeOnly>();

        _valueTypes.Add(typeof(string));
    }

    private static void addValueType<T>() where T : struct
    {
        _valueTypes.Add(typeof(T));
        _valueTypes.Add(typeof(T?));
    }

    public static string CorrectJsonPathOperator(this string op)
    {
        return op == "=" ? "==" : op;
    }

    public static bool IsValueTypeForQuerying(this Type type)
    {
        return type.IsSimple() || _valueTypes.Contains(type) || type.IsEnum ||
               (type.IsNullable() && type.GetInnerTypeFromNullable().IsEnum);
    }

    public static IQueryableMember MemberFor<T>(this IQueryableMemberCollection collection,
        Expression<Func<T, object>> propertyExpression)
    {
        var members = MemberFinder.Determine(propertyExpression);
        return collection.MemberFor(members);
    }

    public static IQueryableMember MemberFor(this IQueryableMemberCollection collection,
        string memberName)
    {
        var member = collection.ElementType.GetProperty(memberName) ??
                     (MemberInfo)collection.ElementType.GetField(memberName);

        return collection.FindMember(member);
    }

    // Assume there's a separate member for the usage of a member with methods
    // i.e., StringMember.ToLower()
    // Dictionary fields will create a separate "dictionary value field"
    public static IQueryableMember MemberFor(this IHasChildrenMembers collection, Expression expression)
    {
        if (expression is ParameterExpression)
        {
            if (collection is IValueCollectionMember collectionMember) return collectionMember.Element;

            return new RootMember(expression.Type);
        }

        var members = MemberFinder.Determine(expression);
        if (!members.Any())
        {
            throw new BadLinqExpressionException("Unable to find any queryable members in expression " + expression);
        }

        var member = collection.FindMember(members[0]);

        for (var i = 1; i < members.Length; i++)
        {
            if (member is IHasChildrenMembers m)
            {
                member = m.FindMember(members[i]);
            }
            else
            {
                throw new BadLinqExpressionException("Marten can not (yet) deal with " + expression);
            }
        }

        return member;
    }

    // Assume there's a separate member for the usage of a member with methods
    // i.e., StringMember.ToLower()
    // Dictionary fields will create a separate "dictionary value field"
    public static IQueryableMember MemberFor(this IHasChildrenMembers collection, Expression expression,
        string invalidExpression)
    {
        if (collection is IValueCollectionMember valueCollection)
        {
            return valueCollection.Element;
        }

        if (expression is ParameterExpression)
        {
            return new RootMember(expression.Type);
        }

        var members = MemberFinder.Determine(expression, invalidExpression);
        if (!members.Any())
        {
            throw new BadLinqExpressionException("Unable to find any queryable members in expression " + expression);
        }

        var member = collection.FindMember(members[0]);

        for (var i = 1; i < members.Length; i++)
        {
            if (member is IHasChildrenMembers m)
            {
                member = m.FindMember(members[i]);
            }
            else
            {
                throw new BadLinqExpressionException("Marten can not (yet) deal with " + expression);
            }
        }

        return member;
    }

    public static IQueryableMember MemberFor(this IHasChildrenMembers collection, MemberInfo[] members)
    {
        var member = collection.FindMember(members[0]);
        for (var i = 1; i < members.Length; i++)
        {
            if (member is IHasChildrenMembers m)
            {
                member = m.FindMember(members[i]);
            }
            else
            {
                throw new BadLinqExpressionException("Marten does not (yet) support using member chain: " +
                                                     members.Select(x => x.Name).Join("."));
            }
        }

        return member;
    }

    public static ISqlFragment ReduceToValue(this IQueryableMemberCollection collection, Expression expression)
    {
        if (expression is ConstantExpression c)
        {
            return new CommandParameter(c);
        }

        if (expression is MemberExpression m)
        {
            if (m.Expression is ConstantExpression)
            {
                var lambdaWithoutParameters =
                    Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object)));
                var compiledLambda = lambdaWithoutParameters.CompileFast();

                var value = compiledLambda();
                return new CommandParameter(value);
            }

            return collection.MemberFor(expression);
        }

        throw new BadLinqExpressionException("Marten does not (yet) know how to process a Linq value for " +
                                             expression);
    }

    public static bool IsCompilableExpression(this MemberExpression node)
    {
        return (node.Expression is ConstantExpression || node.Expression != null) &&
               node.Expression.ToString().StartsWith("value(");
    }

    public static bool TryToParseConstant(this Expression expression, out ConstantExpression constant)
    {
        if (expression == null)
        {
            constant = default;
            return false;
        }

        if (expression is ConstantExpression c)
        {
            constant = ReduceToConstant(expression);
            return true;
        }

        if (expression is MemberExpression m && m.IsCompilableExpression())
        {
            constant = ReduceToConstant(expression);
            return true;
        }

        constant = default;
        return false;
    }

    /// <summary>
    ///     Write out the JSONPath locator for the current member within its collection
    /// </summary>
    /// <param name="member"></param>
    /// <param name="builder"></param>
    public static void WriteJsonPath(this IQueryableMember member, CommandBuilder builder)
    {
        foreach (var ancestor in member.Ancestors)
        {
            if (ancestor.JsonPathSegment.IsNotEmpty())
            {
                builder.Append(ancestor.JsonPathSegment);
                builder.Append(".");
            }
        }

        builder.Append(member.JsonPathSegment);
    }

    public static IEnumerable<string> JsonPathSegments(this IQueryableMember member)
    {
        foreach (var ancestor in member.Ancestors)
        {
            if (ancestor.JsonPathSegment.IsNotEmpty())
            {
                yield return ancestor.JsonPathSegment;
            }
        }

        yield return member.JsonPathSegment;
    }

    public static string WriteJsonPath(this IQueryableMember member)
    {
        // I judged it unnecessary to use a StringBuilder
        var jsonPath = "";
        foreach (var ancestor in member.Ancestors)
        {
            if (ancestor.JsonPathSegment.IsNotEmpty())
            {
                jsonPath += ancestor.JsonPathSegment;
                jsonPath += ".";
            }
        }

        jsonPath += member.JsonPathSegment;
        return jsonPath;
    }

    public static string AddJsonPathParameter(this IDictionary<string, object> dict, object value)
    {
        var name = "val" + (dict.Count + 1);
        dict[name] = value;
        return "$" + name;
    }

    private static readonly Type[] valueExpressionTypes =
    {
        typeof(ConstantExpression)
    };

    public static object Value(this Expression expression)
    {
        if (expression is ConstantExpression c)
        {
            return c.Value;
        }

        return ReduceToConstant(expression).Value;
    }

    public static bool IsValueExpression(this Expression expression)
    {
        if (expression == null)
        {
            return false;
        }

        return valueExpressionTypes.Any(t => t.IsInstanceOfType(expression)) ||
               expression.NodeType == ExpressionType.Lambda;
    }

    internal static ConstantExpression ReduceToConstant(this Expression expression)
    {
        if (expression is LambdaExpression l)
        {
            expression = l.Body.As<BinaryExpression>().Right;
        }

        if (expression.NodeType == ExpressionType.Constant)
        {
            var constantExpression = (ConstantExpression)expression;
            var valueAsIQueryable = constantExpression.Value as IQueryable;
            if (valueAsIQueryable != null && valueAsIQueryable.Expression != constantExpression)
            {
                return (ConstantExpression)valueAsIQueryable.Expression;
            }

            return constantExpression;
        }

        var lambdaWithoutParameters = Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object)));
        var compiledLambda = lambdaWithoutParameters.CompileFast();

        try
        {
            var value = compiledLambda();
            return Expression.Constant(value, expression.Type);
        }
        catch (Exception e)
        {
            throw new BadLinqExpressionException(
                "Error while trying to find a value for the Linq expression " + expression, e);
        }
    }
}
