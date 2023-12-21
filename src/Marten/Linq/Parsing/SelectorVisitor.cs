using System;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Parsing;

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
                 member.MemberType == typeof(DateTimeOffset))
        {
            _statement.SelectClause =
                typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(member,
                    _statement.SelectClause.FromObject,
                    member.MemberType);
        }
        else
        {
            _statement.SelectClause =
                typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                    member.RawLocator,
                    member.MemberType);
        }
    }

    public void ToSelectTransform(Expression selectExpression, ISerializer serializer)
    {
        var visitor = new SelectParser(_serializer, _collection, selectExpression);

        _statement.SelectClause =
            typeof(SelectDataSelectClause<>).CloseAndBuildAs<ISelectClause>(_statement.SelectClause.FromObject,
                visitor.NewObject,
                selectExpression.Type);
    }
}
