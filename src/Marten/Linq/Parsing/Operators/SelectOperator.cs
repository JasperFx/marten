#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class SelectOperator: LinqOperator
{
    public SelectOperator(): base("Select")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        // Capture the current usage before CollectionUsageFor potentially creates a new one.
        // Due to outermost-to-innermost expression tree traversal, any WhereExpressions
        // already on the current usage were added by operators that come AFTER Select in
        // user code (e.g., .Select(...).Where(...)) and need to be hoisted.
        var previousUsage = query.CurrentUsage;

        var select = expression.Arguments.Last();
        LambdaExpression? selectLambda = null;
        if (select is UnaryExpression e)
        {
            select = e.Operand;
        }

        if (select is LambdaExpression l)
        {
            selectLambda = l;
            select = l.Body;
        }

        var usage = query.CollectionUsageFor(expression);

        // Expression hoisting for .Select().Where() chains (GH-3009).
        // When the select body is a simple member access (e.g., x => x.Inner), we can
        // rewrite post-Select Where expressions to reference the original document type
        // by prepending the select member path.
        if (selectLambda != null && select is MemberExpression memberSelect && previousUsage != null
            && previousUsage.WhereExpressions.Any())
        {
            if (previousUsage == usage)
            {
                // Same-type case: Select projects to the same type (e.g., Target.Inner is Target).
                // Force a new CollectionUsage so post-Select wheres are separated from pre-Select ones.
                usage = query.StartNewCollectionUsageFor(expression);
            }

            // Rewrite and move Where expressions from the post-Select usage to the document usage
            HoistWhereExpressions(previousUsage, usage, previousUsage.ElementType, memberSelect);
        }

        usage.SelectExpression = select;
    }

    internal static void HoistWhereExpressions(
        CollectionUsage source, CollectionUsage target,
        Type projectedType, MemberExpression selectBody)
    {
        var rewriter = new PostSelectExpressionRewriter(projectedType, selectBody);

        foreach (var where in source.WhereExpressions)
        {
            target.WhereExpressions.Add(rewriter.Visit(where));
        }

        source.WhereExpressions.Clear();
    }

    /// <summary>
    /// Rewrites expressions from a post-Select Where clause by replacing ParameterExpression
    /// references of the projected type with the Select body (a MemberExpression).
    /// For example, transforms <c>y.Value > 5</c> (where y is the projected type) into
    /// <c>x.Inner.Value > 5</c> (where x.Inner is the Select body).
    /// </summary>
    private class PostSelectExpressionRewriter: ExpressionVisitor
    {
        private readonly Type _projectedType;
        private readonly MemberExpression _selectBody;

        public PostSelectExpressionRewriter(Type projectedType, MemberExpression selectBody)
        {
            _projectedType = projectedType;
            _selectBody = selectBody;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Type == _projectedType)
            {
                return _selectBody;
            }

            return base.VisitParameter(node);
        }
    }
}
