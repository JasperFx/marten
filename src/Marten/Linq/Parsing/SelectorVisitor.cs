// visitor usage is impossible to annotate
#nullable disable
using System;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Util;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Parsing;

public class SelectorVisitor: ExpressionVisitor
{
    private readonly IQueryableMemberCollection _collection;
    private readonly ISerializer _serializer;
    private readonly SelectorStatement _statement;
    private bool _visiting;

    public SelectorVisitor(SelectorStatement statement, IQueryableMemberCollection collection, ISerializer serializer)
    {
        _statement = statement;
        _collection = collection;
        _serializer = serializer;
    }

    public override Expression Visit(Expression node)
    {
        // #5943: a projection that reads nothing from the document, `Select(x => Lookup.Grid[1][1])` or
        // `Select(x => captured)`, is a value. Walking it as a member chain looked a static or captured
        // member up on the document instead, which returned default values, or threw ArgumentNullException
        // ('array') through an array index. It goes to the client-side projection, which reads the value.
        // An object built from values alone still goes through ToSelectTransform, which binds each one.
        // Only the whole projection: a closed part of a larger one, an index say, is not the projection.
        var isWholeProjection = !_visiting;
        _visiting = true;

        if (isWholeProjection && node != null && node is not (NewExpression or MemberInitExpression) &&
            node.IsClosedValue())
        {
            projectOnClient(node);
            return null;
        }

        return base.Visit(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == nameof(QueryableExtensions.ExplicitSql))
        {
            var sql = (string)node.Arguments.Last().ReduceToConstant().Value;

            if (node.Type == typeof(string))
            {
                _statement.SelectClause =
                    new NewScalarStringSelectClause(sql, _statement.SelectClause.FromObject);
            }
            else if (node.Type.IsSimple() || node.Type == typeof(Guid) ||
                     node.Type == typeof(decimal) ||
                     node.Type == typeof(DateTimeOffset))
            {
                _statement.SelectClause =
                    SelectClauseResolvers.NewScalar(node.Type, sql, _statement.SelectClause.FromObject);
            }
            else
            {
                _statement.SelectClause =
                    SelectClauseResolvers.Data(node.Type, _statement.SelectClause.FromObject, sql);
            }
            return null;
        }

        // #5949: Select(x => x.List[1]) or Select(x => x.Values.ElementAt(1))
        if (node.IsElementIndexer())
        {
            ToScalar(node);
            return null;
        }

        return base.VisitMethodCall(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        ToScalar(node);
        return null;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        ToScalar(node);
        return null;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        ToSelectTransform(node, _serializer);
        return null;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        ToSelectTransform(node, _serializer);
        return null;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // #5949: Select(x => x.Values[1]) or Select(x => x.Grid[1][0]). Visiting the children of the index
        // instead rebuilt the node from what they returned, null, and threw ArgumentNullException ('array').
        if (node.NodeType == ExpressionType.ArrayIndex)
        {
            ToScalar(node);
            return null;
        }

        return base.VisitBinary(node);
    }

    public void ToScalar(Expression selectClauseSelector)
    {
        // #5949: MemberFinder cannot follow an index, so x.Values[1] or x.Children[0].Name resolves the way the
        // same chain does in a Where() comparison. Every other chain resolves exactly as before.
        var member = selectClauseSelector.HasElementIndex()
            ? _collection.MemberForIndexedChain(selectClauseSelector)
            : _collection.MemberFor(selectClauseSelector);

        if (member.MemberType == typeof(string))
        {
            _statement.SelectClause =
                new NewScalarStringSelectClause(member.RawLocator, _statement.SelectClause.FromObject);
        }
        else if (member.MemberType.IsSimple() || member.MemberType == typeof(Guid) ||
                 member.MemberType == typeof(decimal) ||
                 member.MemberType == typeof(DateTimeOffset) || member.MemberType == typeof(DateTime) ||
                 member.MemberType == typeof(DateOnly) || member.MemberType == typeof(TimeOnly))
        {
            // DateOnly/TimeOnly read their native date/time TypedLocator here; the
            // DataSelectClause fallback would JSON-deserialize the raw ->> text and fail.
            _statement.SelectClause = SelectClauseResolvers.NewScalar(member.MemberType, member,
                _statement.SelectClause.FromObject);
        }
        else
        {
            _statement.SelectClause =
                // #5589: a type test and a direct call, not a reflective method lookup.
                member is IValueTypeMember valueTypeMember
                ? valueTypeMember.BuildSelectClause(_statement.FromObject)
                : SelectClauseResolvers.Data(member.MemberType, _statement.FromObject, member.RawLocator);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer. #5624: a projection into an ANONYMOUS type is the one shape that contract cannot cover -- no [JsonSerializable] attribute can name one -- so Marten refuses that query with the remedy rather than leaning on this suppression.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. The select clauses closed over a PROJECTED type now resolve through SelectClauseResolvers, which holds a compile-time instantiation for every type that reached MartenLinqQueryable<T>; the reflective path here remains the fallback for a JIT (#5624). Marten 9 retired the codegen-write step the earlier wording cited.")]
    public void ToSelectTransform(Expression selectExpression, ISerializer serializer)
    {
        try
        {
            var visitor = new SelectParser(_serializer, _collection, selectExpression);

            _statement.SelectClause = SelectClauseResolvers.SelectData(selectExpression.Type,
                _statement.SelectClause.FromObject, visitor.NewObject);
        }
        catch (SelectProjectionNotSimpleException)
        {
            projectOnClient(selectExpression);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Same contract as ToSelectTransform: the GH-5011 client-side projection compiles the Select() body, and CloseAndBuildAs closes LambdaSelectClause over the document and projected types, which flow in from StoreOptions / Schema.For<T>().")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Same contract as ToSelectTransform: Type.MakeGenericType for LambdaSelectClause<,>, and Expression.Compile(), which interprets where dynamic code is unavailable.")]
    private void projectOnClient(Expression selectExpression)
    {
        // GH-5011: the Select() projection contains method calls, arithmetic, casts,
        // or conditional expressions that can't be translated to a server-side
        // jsonb_build_object() expression. Fall back to compiling the original
        // Select() lambda and applying it against the fully deserialized source
        // document on the client -- the same behavior Marten had before that
        // optimization existed.
        // #5943: a projection that reads nothing from the document has no parameter of its own, so it
        // is given one; the value it computes is the same for every row, as in LINQ to Objects.
        var parameter = ParameterFinder.FindIn(selectExpression) ??
                        Expression.Parameter(_collection.ElementType, "x");

        var lambda = Expression.Lambda(selectExpression, parameter);
        var compiled = lambda.Compile();

        // #5233: `compiled` closes over whatever ConstantExpressions the projection reads from.
        // Harmless for an ordinary query -- the expression tree is rebuilt per call -- but a
        // compiled query caches this delegate, so a captured value would be frozen at plan time.
        // Record it here; QueryCompiler is the only caller that cares.
        var closesOverCapturedState = CapturedStateDetector.Detect(selectExpression);

        _statement.SelectClause =
            typeof(LambdaSelectClause<,>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                compiled,
                closesOverCapturedState,
                _collection.ElementType,
                selectExpression.Type);
    }
}

/// <summary>
/// Finds the first ParameterExpression referenced anywhere within an expression tree.
/// Used by the GH-5011 client-side Select() fallback to recover the document parameter
/// (e.g. the `x` in `x => new Dto(...)`) so the projection body can be re-wrapped into a
/// compilable lambda after LinqOperator processing discarded the original
/// LambdaExpression and kept only its body.
/// </summary>
internal sealed class ParameterFinder: ExpressionVisitor
{
    private ParameterExpression _found;

    public static ParameterExpression FindIn(Expression expression)
    {
        var finder = new ParameterFinder();
        finder.Visit(expression);
        return finder._found;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _found ??= node;
        return base.VisitParameter(node);
    }
}


/// <summary>
/// #5233: does this projection read a value off a captured object? A member access whose root is a
/// <see cref="ConstantExpression" /> is a closure field or, inside <c>ICompiledQuery.QueryIs()</c>,
/// a property of the compiled query instance. A static member (null root) and a plain literal are
/// both fine -- neither varies between invocations.
/// </summary>
internal sealed class CapturedStateDetector: ExpressionVisitor
{
    private bool _found;

    public static bool Detect(Expression expression)
    {
        var detector = new CapturedStateDetector();
        detector.Visit(expression);
        return detector._found;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        var root = node.Expression;
        while (root is MemberExpression inner)
        {
            root = inner.Expression;
        }

        if (root is ConstantExpression)
        {
            _found = true;
        }

        return base.VisitMember(node);
    }
}
