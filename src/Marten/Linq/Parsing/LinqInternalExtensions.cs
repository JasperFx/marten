#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
public static class LinqInternalExtensions
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
                     (MemberInfo)collection.ElementType.GetField(memberName)!;

        return collection.FindMember(member);
    }

    // Assume there's a separate member for the usage of a member with methods
    // i.e., StringMember.ToLower()
    // Dictionary fields will create a separate "dictionary value field"
    /// <summary>
    /// Use to find the correct queryable member for an expression. Example: collectionMember.MemberFor(node.Method.Arguments[0])
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="expression"></param>
    /// <returns></returns>
    /// <exception cref="BadLinqExpressionException"></exception>
    public static IQueryableMember MemberFor(this IHasChildrenMembers collection, Expression expression)
    {
        if (expression is ParameterExpression)
        {
            if (collection is IValueCollectionMember collectionMember) return collectionMember.Element;

            return new RootMember(expression.Type);
        }

        var members = MemberFinder.Determine(expression);
        if (members.Length == 0)
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
    /// <summary>
    /// Find the queryable member against a collection using an Expression that represents
    /// the accessor of that expression
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="expression"></param>
    /// <param name="invalidExpression"></param>
    /// <returns></returns>
    /// <exception cref="BadLinqExpressionException"></exception>
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
        if (members.Length == 0)
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
                var compiledLambda = FastExpressionCompiler.ExpressionCompiler.CompileFast(lambdaWithoutParameters);

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
        // A MemberExpression is reducible to a constant at parse time iff its inner
        // subtree contains no free ParameterExpression -- which correctly covers both
        // compiler-emitted closure captures (Property(Constant(<displayClass>), "p"))
        // *and* shapes whose inner is a MethodCall over closure-captured values
        // (e.g. Member(Call(ElementAt, [Member(Constant(<displayClass>), "list"),
        // Constant(2)]), "Date")).
        //
        // The previous heuristic gated on `node.Expression.ToString().StartsWith("value(")`,
        // which incidentally covered MethodCall shapes whose printed form started with
        // the closure access, *but* missed programmatic receivers whose wrapper type
        // overrides ToString() (the #4599 trigger). The "no free parameter" structural
        // check is the precise predicate the heuristic was approximating and handles
        // both cases correctly.
        if (node.Expression == null) return true;

        var finder = new FreeParameterFinder();
        finder.Visit(node.Expression);
        return finder.Found == null;
    }

    public static bool TryToParseConstant(this Expression? expression, [NotNullWhen(true)]out ConstantExpression? constant)
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
    public static void WriteJsonPath(this IQueryableMember member, ICommandBuilder builder)
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
            return c.Value!;
        }

        return ReduceToConstant(expression).Value!;
    }

    public static bool IsValueExpression([NotNullWhen(true)]this Expression? expression)
    {
        if (expression == null)
        {
            return false;
        }

        return valueExpressionTypes.Any(t => t.IsInstanceOfType(expression)) ||
               expression.NodeType == ExpressionType.Lambda;
    }

    public static ConstantExpression ReduceToConstant(this Expression expression)
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

        // Guard: if the expression references a free ParameterExpression (one not
        // bound by a lambda *inside* the expression), wrapping it in our parameter-less
        // Lambda<Func<object>> below produces a body with unbound parameters, and
        // FastExpressionCompiler crashes with the confusing
        //   "variable 'x' of type 'Doc' referenced from scope '', but it is not defined"
        // message. That symptom (see #4599) is almost always a Linq-parser bug at the
        // call site; surface a clear error instead of leaking the FEC message.
        var freeParameterFinder = new FreeParameterFinder();
        freeParameterFinder.Visit(expression);
        if (freeParameterFinder.Found != null)
        {
            var p = freeParameterFinder.Found;
            throw new BadLinqExpressionException(
                $"Marten cannot reduce the expression '{expression}' to a constant because it references the free parameter '{p.Name}' of type '{p.Type.Name}'. This usually indicates a Linq parser issue at the call site.");
        }

        var lambdaWithoutParameters = Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object)));
        var compiledLambda = FastExpressionCompiler.ExpressionCompiler.CompileFast(lambdaWithoutParameters);

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

    /// <summary>
    /// Walks an expression looking for the first <see cref="ParameterExpression"/>
    /// that is *not* bound by a lambda inside the expression itself. Used by
    /// <see cref="ReduceToConstant"/> as a safety net against feeding parameter-bearing
    /// subtrees into the parameter-less lambda + FEC compile path (see #4599).
    /// </summary>
    private sealed class FreeParameterFinder: ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _bound = new();

        public ParameterExpression? Found { get; private set; }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            foreach (var p in node.Parameters) _bound.Add(p);
            var result = base.VisitLambda(node);
            foreach (var p in node.Parameters) _bound.Remove(p);
            return result;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (Found == null && !_bound.Contains(node))
            {
                Found = node;
            }

            return node;
        }
    }
}
