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

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class SelectorVisitor: ExpressionVisitor
{
    private readonly IQueryableMemberCollection _collection;
    private readonly ISerializer _serializer;
    private readonly SelectorStatement _statement;

    public SelectorVisitor(SelectorStatement statement, IQueryableMemberCollection collection, ISerializer serializer)
    {
        _statement = statement;
        _collection = collection;
        _serializer = serializer;
    }

    public override Expression Visit(Expression node)
    {
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
                    typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(sql,
                        _statement.SelectClause.FromObject,
                        node.Type);
            }
            else
            {
                _statement.SelectClause =
                    typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                        sql,
                        node.Type);
            }
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

    public void ToScalar(Expression selectClauseSelector)
    {
        var member = _collection.MemberFor(selectClauseSelector);

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
            _statement.SelectClause =
                typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(member,
                    _statement.SelectClause.FromObject,
                    member.MemberType);
        }
        else
        {
            _statement.SelectClause =
                member.IsGenericInterfaceImplementation(typeof(IValueTypeMember<,>))
                ? (ISelectClause)member.CallGenericInterfaceMethod(typeof(IValueTypeMember<,>), "BuildSelectClause", _statement.FromObject)
                : typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(_statement.FromObject,
                    member.RawLocator,
                    member.MemberType);
        }
    }

    public void ToSelectTransform(Expression selectExpression, ISerializer serializer)
    {
        try
        {
            var visitor = new SelectParser(_serializer, _collection, selectExpression);

            _statement.SelectClause =
                typeof(SelectDataSelectClause<>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                    visitor.NewObject,
                    selectExpression.Type);
        }
        catch (SelectProjectionNotSimpleException)
        {
            // GH-5011: the Select() projection contains method calls, arithmetic, casts,
            // or conditional expressions that can't be translated to a server-side
            // jsonb_build_object() expression. Fall back to compiling the original
            // Select() lambda and applying it against the fully deserialized source
            // document on the client -- the same behavior Marten had before that
            // optimization existed.
            var parameter = ParameterFinder.FindIn(selectExpression);
            if (parameter == null)
            {
                throw new Marten.Exceptions.BadLinqExpressionException(
                    "Marten is not (yet) able to process this Select() transform");
            }

            var lambda = Expression.Lambda(selectExpression, parameter);
            var compiled = lambda.Compile();

            _statement.SelectClause =
                typeof(LambdaSelectClause<,>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                    compiled,
                    _collection.ElementType,
                    selectExpression.Type);
        }
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
