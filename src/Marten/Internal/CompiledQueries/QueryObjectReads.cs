#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Parsing;

namespace Marten.Internal.CompiledQueries;

/// <summary>
///     #5711: refuses a compiled query whose <c>QueryIs()</c> computes a value from the query object
///     instead of reading one of its members directly.
/// </summary>
/// <remarks>
///     <para>
///         The plan binds a SQL parameter to a query member by matching values: a parameter carrying a
///         member's planning value is re-read from that member on every execution, and any other
///         parameter is a literal. A value computed from members, <c>First + Second</c> or
///         <c>Name.ToUpper()</c>, equals no single member, so it was taken for a literal and every later
///         execution silently reused the value the plan was built with.
///     </para>
///     <para>
///         Whatever reads the query object and uses none of the query's lambda parameters is evaluated
///         on the client before it reaches SQL, so each such sub-expression is checked here, at its
///         largest. It is allowed when it is a member read directly off the query object, when it is that
///         read under conversions that leave the member's planning value unchanged (C# lifts an <c>int</c>
///         member to <c>int?</c> to compare it), or when it is an inline array whose elements each pass,
///         which the plan binds element by element. Anything else is refused.
///     </para>
/// </remarks>
internal static class QueryObjectReads
{
    public static void AssertEveryReadBinds(CompiledQueryPlan plan, object query, Expression queryIs,
        StoreOptions options)
    {
        var finder = new ClosedReadFinder(query);
        finder.Visit(queryIs);

        foreach (var read in finder.Reads)
        {
            if (!binds(read, plan, query, options))
            {
                throw new InvalidCompiledQueryException(
                    $"Invalid compiled query type `{query.GetType().FullNameInCode()}`. Its QueryIs() uses `{describe(read, query)}`, a value computed from the query object rather than one of its members read directly. A compiled query is planned once, and it binds each SQL parameter to the member whose value that parameter carries; a computed value matches no member, so it would be frozen at the value it had when the plan was built and every later execution would silently reuse it. Compute the value before running the query and expose it as a member of its own, or run this as a normal LINQ query instead of a compiled one. See https://github.com/JasperFx/marten/issues/5711");
            }
        }
    }

    private static bool binds(Expression read, CompiledQueryPlan plan, object query, StoreOptions options)
    {
        // C# 14 binds `new[] { ... }.Contains(x)` to MemoryExtensions.Contains, so the inline array
        // arrives wrapped in its implicit conversion to a span. The array underneath is what is bound.
        if (read is MethodCallExpression { Method.Name: "op_Implicit", Arguments: [NewArrayExpression wrapped] } call &&
            call.Type.IsGenericType &&
            (call.Type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) ||
             call.Type.GetGenericTypeDefinition() == typeof(Span<>)))
        {
            read = wrapped;
        }

        if (read is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array)
        {
            return array.Expressions.All(element =>
                !ClosedReadFinder.ReadsQuery(element, query) || binds(element, plan, query, options));
        }

        var inner = read;
        while (inner is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs
               } unary)
        {
            inner = unary.Operand;
        }

        if (inner is not MemberExpression member || !isQueryReference(member.Expression, query))
        {
            return false;
        }

        // A plain member read is what the plan was built to bind
        if (ReferenceEquals(inner, read))
        {
            return true;
        }

        // Converted: binds only while the converted value is still the member's planning value, which
        // is what the plan matches by. An enum cast to its number does under integer enum storage and
        // does not under string storage.
        var source = plan.QueryMembers.FirstOrDefault(x => x.Member == member.Member) as IArrayElementSource;
        if (source == null)
        {
            return false;
        }

        var value = read.ReduceToConstant().Value;
        return Equals(value, source.ElementValue(options, false)) || Equals(value, source.ElementValue(options, true));
    }

    private static bool isQueryReference(Expression? expression, object query)
    {
        return expression switch
        {
            ConstantExpression constant => ReferenceEquals(constant.Value, query),

            // A lambda that captures a local as well as the query object reads the query through the
            // closure's captured `this`
            MemberExpression { Expression: ConstantExpression closure, Member: FieldInfo field } =>
                ReferenceEquals(field.GetValue(closure.Value), query),

            _ => false
        };
    }

    private static string describe(Expression read, object query)
    {
        var placeholder = Expression.Parameter(query.GetType(), "query");
        return new QueryReferenceRenamer(query, placeholder).Visit(read)!.ToString();
    }

    /// <summary>
    ///     Collects the largest sub-expressions that read the query object and use no parameter of the
    ///     query's own lambdas: those are evaluated on the client, so their value is what reaches SQL.
    /// </summary>
    private sealed class ClosedReadFinder: ExpressionVisitor
    {
        private readonly object _query;

        public ClosedReadFinder(object query)
        {
            _query = query;
        }

        public List<Expression> Reads { get; } = new();

        public override Expression? Visit(Expression? node)
        {
            if (node == null)
            {
                return null;
            }

            // A lambda declares its own parameters, so it would scan as closed, but a query operator's
            // lambda is translated rather than evaluated. Only the reads inside it are values.
            if (node is LambdaExpression || node.NodeType == ExpressionType.Quote)
            {
                return base.Visit(node);
            }

            var scan = new Scan(_query);
            scan.Visit(node);

            if (scan.ReadsQuery && !scan.UsesOuterParameter)
            {
                Reads.Add(node);
                return node;
            }

            return base.Visit(node);
        }

        public static bool ReadsQuery(Expression expression, object query)
        {
            var scan = new Scan(query);
            scan.Visit(expression);
            return scan.ReadsQuery;
        }
    }

    private sealed class Scan: ExpressionVisitor
    {
        private readonly object _query;
        private readonly HashSet<ParameterExpression> _declared = new();

        public Scan(object query)
        {
            _query = query;
        }

        public bool ReadsQuery { get; private set; }

        // A parameter of an enclosing lambda, so the expression cannot be evaluated on its own
        public bool UsesOuterParameter { get; private set; }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            foreach (var parameter in node.Parameters) _declared.Add(parameter);
            return base.VisitLambda(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (!_declared.Contains(node))
            {
                UsesOuterParameter = true;
            }

            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (ReferenceEquals(node.Value, _query))
            {
                ReadsQuery = true;
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (isQueryReference(node, _query))
            {
                ReadsQuery = true;
                return node;
            }

            return base.VisitMember(node);
        }
    }

    private sealed class QueryReferenceRenamer: ExpressionVisitor
    {
        private readonly object _query;
        private readonly ParameterExpression _placeholder;

        public QueryReferenceRenamer(object query, ParameterExpression placeholder)
        {
            _query = query;
            _placeholder = placeholder;
        }

        public override Expression? Visit(Expression? node)
        {
            return node != null && isQueryReference(node, _query) ? _placeholder : base.Visit(node);
        }
    }
}
