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

        var followsSelectMany = selectLambda != null && FollowsPlainSelectMany(expression);

        // #5933: Select(v => v) after a SelectMany() changes nothing, so leave the query exactly as it would
        // be without it
        if (followsSelectMany && select == selectLambda!.Parameters[0])
        {
            return;
        }

        var usage = query.CollectionUsageFor(expression);

        // #5933: a Select() after SelectMany() that maps an element to the same type -- v => v % 3 over an int
        // collection -- would otherwise share one usage with every operator after it, and with every operator
        // before it, so a Where() or OrderBy() after the projection could not be told apart from one before it.
        // Operators are applied outermost first, so everything already on the current usage comes after this
        // Select(); start a new usage for the Select() and whatever precedes it.
        // #5955: the same holds for a top-level Select() that maps a document to its own type -- x => x.Inner,
        // where Inner is another Target -- whose OrderBy() was otherwise applied to the documents themselves.
        var splitsTheUsage = followsSelectMany ||
                             (selectLambda != null && select != selectLambda.Parameters[0] &&
                              ProjectsTheQueriedDocuments(expression));

        if (splitsTheUsage && previousUsage != null && previousUsage == usage)
        {
            usage = query.StartNewCollectionUsageFor(expression);
        }

        // Expression hoisting for .Select().Where() chains (GH-3009).
        // When the select body is a simple member access (e.g., x => x.Inner), we can
        // rewrite post-Select Where expressions to reference the original document type
        // by prepending the select member path. #5933: not after a SelectMany(), where the projected rows are
        // filtered by the operators that follow the projection, as they are for any other projection.
        if (!followsSelectMany && selectLambda != null && select is MemberExpression memberSelect &&
            previousUsage != null && previousUsage.WhereExpressions.Any())
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

    /// <summary>
    /// #5933: does this Select() project the rows of a plain <c>SelectMany(x => x.Collection)</c>, with only
    /// row-preserving operators (Where, OrderBy, paging, Distinct, other Select()s) in between? A SelectMany()
    /// with a result selector, which is how GroupJoin() and the two-collection overload are flattened, is
    /// compiled elsewhere and is left alone.
    /// </summary>
    internal static bool FollowsPlainSelectMany(MethodCallExpression select)
    {
        var source = select.Arguments[0];
        while (source is MethodCallExpression call)
        {
            switch (call.Method.Name)
            {
                case "SelectMany":
                    return call.Arguments.Count == 2;

                case "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Take"
                    or "Skip" or "Distinct" or "Select":
                    source = call.Arguments[0];
                    break;

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// #5955: does this Select() project the queried documents themselves, with only row-preserving operators
    /// (Where, OrderBy, paging, Distinct) between it and the query's root?
    /// </summary>
    internal static bool ProjectsTheQueriedDocuments(MethodCallExpression select)
    {
        var source = select.Arguments[0];
        while (source is MethodCallExpression call)
        {
            switch (call.Method.Name)
            {
                case "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Take"
                    or "Skip" or "Distinct":
                    source = call.Arguments[0];
                    break;

                default:
                    return false;
            }
        }

        return source is ConstantExpression;
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
