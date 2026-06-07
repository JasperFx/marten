#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

/// <summary>
/// Parses a SelectMany result selector that spans two joined collections (outer and inner).
/// Builds a NewObject (jsonb_build_object) where each member's locator is prefixed with the
/// appropriate CTE alias instead of the default "d.".
/// </summary>
internal class JoinSelectParser: ExpressionVisitor
{
    private readonly ISerializer _serializer;
    private readonly IQueryableMemberCollection _outerMembers;
    private readonly IQueryableMemberCollection _innerMembers;
    private readonly string _outerCteAlias;
    private readonly string _innerCteAlias;

    // Maps GroupJoin result selector parameter members to outer/inner
    // e.g., for (c, orders) => new { c, orders }, "c" -> outer, "orders" -> inner
    private readonly Dictionary<string, bool> _resultSelectorMemberIsOuter = new();

    // The GroupJoin result selector's parameters
    private readonly ParameterExpression _groupJoinOuterParam;
    private readonly ParameterExpression _groupJoinInnerParam;

    // The SelectMany result selector's parameters
    private readonly ParameterExpression _selectManyGroupParam; // the GroupJoin result (temp)
    private readonly ParameterExpression _selectManyElementParam; // the inner element (o)

    private string _currentField;

    public NewObject NewObject { get; private set; }

    // Non-null when the SelectMany result selector is a bare scalar (e.g. (temp, o) => o.Amount)
    // rather than an object projection. Rendered as to_jsonb(<scalar>) so the existing
    // SerializationSelector<T> deserializes it and the DISTINCT machinery applies unchanged.
    public ISqlFragment ScalarProjection { get; private set; }

    // The same scalar without the to_jsonb wrapper -- the native column. Used for aggregates
    // (Sum/Min/Max/Average), which cannot operate on a jsonb value.
    public ISqlFragment ScalarRawProjection { get; private set; }

    // The fragment to render after SELECT: the scalar wrap if present, else the jsonb_build_object.
    public ISqlFragment Projection => ScalarProjection ?? NewObject;

    public JoinSelectParser(
        ISerializer serializer,
        IQueryableMemberCollection outerMembers,
        IQueryableMemberCollection innerMembers,
        string outerCteAlias,
        string innerCteAlias,
        LambdaExpression groupJoinResultSelector,
        LambdaExpression selectManyResultSelector)
    {
        _serializer = serializer;
        _outerMembers = outerMembers;
        _innerMembers = innerMembers;
        _outerCteAlias = outerCteAlias;
        _innerCteAlias = innerCteAlias;

        NewObject = new NewObject(serializer);

        // Parse GroupJoin result selector to understand the mapping
        // Typically: (c, orders) => new { c, orders }
        _groupJoinOuterParam = groupJoinResultSelector.Parameters[0]; // the outer element
        _groupJoinInnerParam = groupJoinResultSelector.Parameters[1]; // the grouped inner collection

        // Build mapping from result selector's anonymous type members to outer/inner
        ParseGroupJoinResultSelector(groupJoinResultSelector);

        // Parse SelectMany result selector
        // Typically: (temp, o) => new { temp.c.Name, o.Amount }
        _selectManyGroupParam = selectManyResultSelector.Parameters[0]; // the GroupJoin result
        _selectManyElementParam = selectManyResultSelector.Parameters[1]; // the inner element

        Visit(selectManyResultSelector.Body);

        // A bare scalar result selector — (temp, o) => o.Amount — adds nothing to NewObject
        // (VisitMember early-returns because _currentField is never set by a New/MemberInit).
        // Resolve it as a single scalar fragment wrapped in to_jsonb(...).
        if (NewObject.Members.Count == 0)
        {
            ResolveScalarProjection(selectManyResultSelector.Body);
        }
    }

    private void ResolveScalarProjection(Expression body)
    {
        // Strip compiler-inserted Convert/TypeAs (e.g. (decimal?)o.Amount).
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } unary)
        {
            body = unary.Operand;
        }

        var classification = ClassifyMemberAccess(body);
        if (classification == null)
        {
            throw new BadLinqExpressionException(
                "Marten cannot translate this GroupJoin/SelectMany result selector. Project into an "
                + "anonymous type or record (for example (x, o) => new { x.c.Id, o.Amount }) for shapes "
                + "other than a single outer/inner member access.");
        }

        ScalarRawProjection = ResolveMember(body, classification.Value.isOuter);

        // Wrap in to_jsonb so the rendered 'data' column is a JSON value the SerializationSelector
        // can deserialize back to the scalar T (a raw text/uuid locator would not be valid JSON).
        ScalarProjection = new ToJsonbScalarFragment(ScalarRawProjection);
    }

    private void ParseGroupJoinResultSelector(LambdaExpression resultSelector)
    {
        // Handle: (c, orders) => new { c, orders }
        // The body is a NewExpression with arguments that are the parameters
        if (resultSelector.Body is NewExpression newExpr)
        {
            var parameters = newExpr.Constructor.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                var arg = newExpr.Arguments[i];
                var memberName = parameters[i].Name;

                if (arg == _groupJoinOuterParam)
                {
                    _resultSelectorMemberIsOuter[memberName] = true;
                }
                else if (arg == _groupJoinInnerParam)
                {
                    _resultSelectorMemberIsOuter[memberName] = false;
                }
            }
        }
        else if (resultSelector.Body is MemberInitExpression memberInit)
        {
            foreach (var binding in memberInit.Bindings.OfType<MemberAssignment>())
            {
                if (binding.Expression == _groupJoinOuterParam)
                {
                    _resultSelectorMemberIsOuter[binding.Member.Name] = true;
                }
                else if (binding.Expression == _groupJoinInnerParam)
                {
                    _resultSelectorMemberIsOuter[binding.Member.Name] = false;
                }
            }
        }
    }

    /// <summary>
    /// Determines if a member access expression refers to the outer collection.
    /// Returns true for outer, false for inner, null if unknown.
    /// Also returns the remaining expression (after stripping the outer/inner prefix).
    /// </summary>
    private (bool isOuter, Expression memberExpr)? ClassifyMemberAccess(Expression expression)
    {
        // Case 1: Direct inner parameter: o.Amount => inner
        if (expression is MemberExpression directMember &&
            directMember.Expression == _selectManyElementParam)
        {
            return (false, expression);
        }

        // Case 2: Direct inner parameter itself: o => inner (whole entity)
        if (expression == _selectManyElementParam)
        {
            return (false, expression);
        }

        // Case 3: Access through GroupJoin result: temp.c.Name => outer
        // Walk up the member expression chain to find the root
        if (expression is MemberExpression memberChain)
        {
            var chain = new List<MemberExpression>();
            var current = memberChain;

            while (current != null)
            {
                chain.Add(current);
                current = current.Expression as MemberExpression;
            }

            // The chain is [Name, c, temp] reading from innermost to outermost
            // We need to find where temp.X maps to outer or inner
            chain.Reverse(); // Now: [temp, c, Name] or similar

            // Find the first member that's accessed on the selectManyGroupParam (temp)
            if (chain.Count >= 1 && chain[0].Expression == _selectManyGroupParam)
            {
                var groupMemberName = chain[0].Member.Name;

                if (_resultSelectorMemberIsOuter.TryGetValue(groupMemberName, out var isOuter))
                {
                    return (isOuter, expression);
                }
            }
        }

        // Case 4: GroupJoin result param directly: temp => neither (shouldn't happen in projection)
        if (expression == _selectManyGroupParam)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Resolves a member access to an ISqlFragment with the correct CTE alias.
    /// </summary>
    private ISqlFragment ResolveMember(Expression expression, bool isOuter)
    {
        var members = isOuter ? _outerMembers : _innerMembers;
        var cteAlias = isOuter ? _outerCteAlias : _innerCteAlias;

        // Strip the GroupJoin result navigation to get the actual member path
        var memberExpr = StripGroupJoinNavigation(expression, isOuter);

        if (memberExpr == null)
        {
            // Whole entity reference (e.g., just "c" or "o")
            return new CteAliasedFragment(cteAlias, "d.data");
        }

        var member = members.MemberFor(memberExpr);
        return new CteAliasedFragment(cteAlias, member.TypedLocator);
    }

    /// <summary>
    /// Strips the GroupJoin result navigation (temp.c) to get the member expression
    /// relative to the document type (e.g., temp.c.Name -> a synthetic x.Name expression).
    /// </summary>
    private Expression StripGroupJoinNavigation(Expression expression, bool isOuter)
    {
        if (expression == _selectManyElementParam)
        {
            return null; // whole inner entity
        }

        if (expression is MemberExpression memberExpr)
        {
            // Direct access on inner param: o.Name
            if (memberExpr.Expression == _selectManyElementParam)
            {
                // Rewrite to use a fresh parameter so MemberFor can resolve it
                var param = Expression.Parameter(
                    isOuter ? _outerMembers.ElementType : _innerMembers.ElementType, "x");
                return Expression.MakeMemberAccess(param, memberExpr.Member);
            }

            // Access through GroupJoin result: temp.c.Name or temp.c.Inner.Name
            if (memberExpr.Expression is MemberExpression parentMember &&
                parentMember.Expression == _selectManyGroupParam)
            {
                // parentMember is temp.c, memberExpr is temp.c.Name
                // Rewrite to x.Name
                var param = Expression.Parameter(
                    isOuter ? _outerMembers.ElementType : _innerMembers.ElementType, "x");
                return Expression.MakeMemberAccess(param, memberExpr.Member);
            }

            // Deeper nesting: temp.c.Inner.Name
            // Walk up to find the GroupJoin result navigation point
            var chain = new List<MemberExpression>();
            var current = memberExpr;
            while (current != null)
            {
                chain.Add(current);
                if (current.Expression is MemberExpression parent2 &&
                    parent2.Expression == _selectManyGroupParam)
                {
                    // Found the root: parent2 is temp.c
                    // chain has the members from leaf to root (after temp.c)
                    // Rebuild the chain on a fresh parameter
                    var param = Expression.Parameter(
                        isOuter ? _outerMembers.ElementType : _innerMembers.ElementType, "x");
                    Expression result = param;
                    // chain[0] = deepest member, chain[^1] is the one right after temp.c
                    // We need to rebuild from chain[^1] down to chain[0]
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        result = Expression.MakeMemberAccess(result, chain[i].Member);
                    }
                    return result;
                }
                current = current.Expression as MemberExpression;
            }
        }

        return expression;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        var parameters = node.Constructor.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            _currentField = SelectParser.ResolveFieldName(node, parameters, i);
            Visit(node.Arguments[i]);
        }
        return node;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        Visit(node.NewExpression);
        foreach (var binding in node.Bindings.OfType<MemberAssignment>())
        {
            _currentField = binding.Member.Name;
            Visit(binding.Expression);
        }
        return null;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (_currentField == null) return base.VisitMember(node);

        var classification = ClassifyMemberAccess(node);
        if (classification.HasValue)
        {
            NewObject.Members[_currentField] = ResolveMember(node, classification.Value.isOuter);
            _currentField = null;
            return null;
        }

        return base.VisitMember(node);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (_currentField == null) return base.VisitParameter(node);

        // Handle case where the entire entity is projected: (temp, o) => new { Outer = temp.c, Inner = o }
        if (node == _selectManyElementParam)
        {
            NewObject.Members[_currentField] = new CteAliasedFragment(_innerCteAlias, "d.data");
            _currentField = null;
            return null;
        }

        return base.VisitParameter(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        // Handle Convert/TypeAs expressions (e.g., (decimal?)o?.Amount)
        if (node.NodeType == ExpressionType.Convert || node.NodeType == ExpressionType.TypeAs)
        {
            return Visit(node.Operand);
        }

        return base.VisitUnary(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        // Handle null-conditional patterns that the compiler generates for nullable access
        // in left joins, e.g., o == null ? null : o.Amount
        // Just visit the true branch (the actual member access)
        if (_currentField != null)
        {
            Visit(node.IfFalse is DefaultExpression ? node.IfTrue : node.IfFalse);
            return null;
        }

        return base.VisitConditional(node);
    }
}

/// <summary>
/// An ISqlFragment that renders a SQL locator with a CTE alias replacing the default "d." prefix.
/// </summary>
internal class CteAliasedFragment: ISqlFragment
{
    private readonly string _sql;

    public CteAliasedFragment(string cteAlias, string originalLocator)
    {
        // Replace the "d." prefix used by IQueryableMember locators with the CTE alias
        if (originalLocator.StartsWith("d."))
        {
            _sql = cteAlias + "." + originalLocator.Substring(2);
        }
        else if (originalLocator.Contains("d."))
        {
            // For expressions like CAST(d.data ->> 'X' as type)
            _sql = originalLocator.Replace("d.", cteAlias + ".");
        }
        else
        {
            _sql = cteAlias + "." + originalLocator;
        }
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }
}

/// <summary>
/// Wraps a scalar SQL fragment in <c>to_jsonb(...)</c> so a bare scalar GroupJoin/SelectMany
/// projection renders a JSON value in the "data" column that <see cref="Marten.Linq.Selectors.SerializationSelector{T}"/>
/// can deserialize back to the scalar type.
/// </summary>
internal class ToJsonbScalarFragment: ISqlFragment
{
    private readonly ISqlFragment _inner;

    public ToJsonbScalarFragment(ISqlFragment inner)
    {
        _inner = inner;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("to_jsonb(");
        _inner.Apply(builder);
        builder.Append(")");
    }
}

/// <summary>
/// Reduces a member access on a GroupJoin/SelectMany result-selector's projected anon type
/// back into the (outer, inner) parameter space the projection was built from. The SelectMany
/// result selector body builds an anon type (or a MemberInit) whose members map to source
/// expressions in terms of the SelectMany (x, c) parameters; this visitor walks an arbitrary
/// expression and replaces any <c>z.MemberName</c> access on a parameter of the projection
/// type with the corresponding source expression. Used so a post-SelectMany .Select(z =&gt; z.X)
/// or .Sum(z =&gt; z.X) collapses cleanly back into the join's projection space.
///
/// Recognises both anon-type binding shapes:
/// * <c>new { c.Amount, x.p.Name }</c> — NewExpression carries the member names on its Members property
/// * <c>new Row { Amount = c.Amount }</c> — MemberInitExpression carries them as MemberAssignment bindings
/// </summary>
internal sealed class AnonProjectionExpander: ExpressionVisitor
{
    private readonly Dictionary<string, Expression> _memberToSource = new(StringComparer.Ordinal);
    private readonly Type _projectionType;

    public AnonProjectionExpander(LambdaExpression flattenedResultSelector)
    {
        _projectionType = flattenedResultSelector.ReturnType;
        ExtractBindings(flattenedResultSelector.Body);
    }

    /// <summary>True if the result selector body was a NewExpression / MemberInit we could index.</summary>
    public bool CanExpand => _memberToSource.Count > 0;

    /// <summary>Rewrites <paramref name="body"/>, replacing every <c>z.X</c> (where z is the projection type) with the bound source expression.</summary>
    public Expression Expand(Expression body) => Visit(body);

    private void ExtractBindings(Expression body)
    {
        if (body is NewExpression newExpr)
        {
            // Anon types: prefer the Members property (anonymous-type member infos), fall back to
            // constructor parameter names. Records / explicit ctors only expose parameter names.
            if (newExpr.Members != null)
            {
                for (var i = 0; i < newExpr.Members.Count; i++)
                {
                    _memberToSource[newExpr.Members[i].Name] = newExpr.Arguments[i];
                }
            }
            else
            {
                var parameters = newExpr.Constructor!.GetParameters();
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].Name is { } name)
                    {
                        _memberToSource[name] = newExpr.Arguments[i];
                    }
                }
            }
        }
        else if (body is MemberInitExpression memberInit)
        {
            ExtractBindings(memberInit.NewExpression);
            foreach (var binding in memberInit.Bindings.OfType<MemberAssignment>())
            {
                _memberToSource[binding.Member.Name] = binding.Expression;
            }
        }
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // z.MemberName where z is *any* parameter of the projection's anon type.
        // We match by Type rather than reference because the SelectExpression body
        // stored on the CollectionUsage has lost the original lambda parameter.
        if (node.Expression is ParameterExpression p && p.Type == _projectionType
            && _memberToSource.TryGetValue(node.Member.Name, out var source))
        {
            return source;
        }

        return base.VisitMember(node);
    }
}

/// <summary>
/// Classifies an expanded post-SelectMany Where filter by which side of the join its
/// parameter references touch (outer, inner, both, or neither). Used to decide whether
/// the filter can be routed onto the inner CTE's existing WhereExpressions pipeline
/// or needs the (not-yet-implemented) outer / cross-CTE handling.
/// </summary>
internal static class PostSelectManyFilterSideAnalyzer
{
    [Flags]
    public enum Side
    {
        None = 0,
        Outer = 1,
        Inner = 2,
        Both = Outer | Inner,
        InnerOnly = Inner,
        OuterOnly = Outer,
    }

    public static Side Analyze(Expression expression, IReadOnlyList<ParameterExpression> selectManyParameters)
    {
        var visitor = new SideVisitor(selectManyParameters[0], selectManyParameters[1]);
        visitor.Visit(expression);
        return visitor.Touched;
    }

    private sealed class SideVisitor: ExpressionVisitor
    {
        private readonly ParameterExpression _outer;
        private readonly ParameterExpression _inner;
        public Side Touched { get; private set; }

        public SideVisitor(ParameterExpression outer, ParameterExpression inner)
        {
            _outer = outer;
            _inner = inner;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _outer) Touched |= Side.Outer;
            else if (node == _inner) Touched |= Side.Inner;
            return node;
        }
    }
}

/// <summary>
/// Substitutes one ParameterExpression for another in an expression tree. Used to retarget
/// a hoisted post-SelectMany inner-side Where filter from the SelectMany lambda's <c>c</c>
/// parameter onto a fresh parameter of the inner document type, so MemberFor's standard
/// pipeline binds it cleanly without needing to know about the original SelectMany lambda.
/// </summary>
internal sealed class ParameterReplacingVisitor: ExpressionVisitor
{
    private readonly ParameterExpression _from;
    private readonly ParameterExpression _to;

    public ParameterReplacingVisitor(ParameterExpression from, ParameterExpression to)
    {
        _from = from;
        _to = to;
    }

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _from ? _to : base.VisitParameter(node);
}

/// <summary>
/// Strips the GroupJoin result selector's anon-type navigation off a hoisted post-SelectMany
/// outer-side Where filter, so the resulting expression binds against the outer document type
/// directly via <see cref="Marten.Linq.Members.IQueryableMemberCollection.MemberFor"/>.
///
/// The GroupJoin result selector typically looks like <c>(p, children) =&gt; new { p, children }</c>,
/// so the SelectMany's outer parameter <c>x</c> has the anon type. A user filter
/// <c>z.OuterMember == "a"</c> expands through the FlattenedResultSelector to <c>x.p.OuterMember</c>;
/// this visitor rewrites <c>x.p</c> (the anon member access of the outer-side binding) to a
/// fresh <c>outerParam</c> of the outer document type, leaving the trailing member access intact.
/// </summary>
internal sealed class GroupJoinOuterNavigationStripper: ExpressionVisitor
{
    private readonly ParameterExpression _selectManyOuterParam;
    private readonly ParameterExpression _outerDocParam;
    // The anonymous-type member name on x whose value is the outer document, eg. "p".
    private readonly string? _outerMemberName;

    public GroupJoinOuterNavigationStripper(
        ParameterExpression selectManyOuterParam,
        LambdaExpression groupJoinResultSelector,
        ParameterExpression outerDocParam)
    {
        _selectManyOuterParam = selectManyOuterParam;
        _outerDocParam = outerDocParam;
        _outerMemberName = FindOuterMember(groupJoinResultSelector);
    }

    private static string? FindOuterMember(LambdaExpression resultSelector)
    {
        // (outer, inner) => new { p = outer, children = inner }: walk the anon-type bindings and
        // return the member name whose value is the GroupJoin outer parameter.
        var outerParam = resultSelector.Parameters[0];
        if (resultSelector.Body is NewExpression newExpr)
        {
            for (var i = 0; i < newExpr.Arguments.Count; i++)
            {
                if (newExpr.Arguments[i] == outerParam)
                {
                    return newExpr.Members != null
                        ? newExpr.Members[i].Name
                        : newExpr.Constructor!.GetParameters()[i].Name;
                }
            }
        }
        else if (resultSelector.Body is MemberInitExpression memberInit)
        {
            foreach (var binding in memberInit.Bindings.OfType<MemberAssignment>())
            {
                if (binding.Expression == outerParam)
                {
                    return binding.Member.Name;
                }
            }
        }

        return null;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // x.p  ->  outerDocParam
        if (node.Expression == _selectManyOuterParam
            && _outerMemberName != null
            && node.Member.Name == _outerMemberName)
        {
            return _outerDocParam;
        }

        return base.VisitMember(node);
    }
}
