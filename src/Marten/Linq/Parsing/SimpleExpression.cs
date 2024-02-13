using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;


internal class SimpleExpression: ExpressionVisitor
{
    private readonly Expression _expression;
    private readonly IQueryableMemberCollection _queryableMembers;

    public List<MemberInfo> Members = new();

    public SimpleExpression(IQueryableMemberCollection queryableMembers, Expression expression)
    {
        if (expression is LambdaExpression l) expression = l.Body;

        _expression = expression;
        _queryableMembers = queryableMembers;
        switch (expression)
        {
            case ConstantExpression c:
                Constant = c;
                HasConstant = true;
                break;

            case NewExpression n:
                Constant = n.ReduceToConstant();
                HasConstant = true;
                break;

            case ParameterExpression:
                if (queryableMembers is IValueCollectionMember collection)
                {
                    Member = collection.Element;
                    return;
                }

                break;
            default:
                try
                {
                    Visit(expression);
                }
                catch (BadLinqExpressionException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new BadLinqExpressionException($"Whoa pardner, Marten could not parse '{expression}' with the SimpleExpression construct", e);
                }
                break;
        }

        if (HasConstant)
        {
            Members.Clear();
        }
        else if (Comparable != null || Member != null)
        {
            return;
        }
        else if (!FoundParameterAtStart)
        {
            HasConstant = true;
            Constant = _expression.ReduceToConstant();
            Members.Clear();
        }
        else
        {
            Member = queryableMembers.MemberFor(Members.ToArray());
        }
    }

    public override Expression Visit(Expression node)
    {
        return base.Visit(node);
    }

    // Pretend for right now that there's only one of all of these
    // obviously won't be true forever
    public ConstantExpression Constant { get; set; }
    public IQueryableMember Member { get; private set; }
    public IComparableMember Comparable { get; private set; }
    public bool FoundParameterAtStart { get; private set; }
    public List<ISqlFragment> Filters { get; } = new();

    public bool HasConstant { get; set; }

    public ISqlFragment CompareTo(SimpleExpression right, string op)
    {
        // See GH-2895
        if (Constant != null)
        {
            if (right.Constant != null)
            {
                return new ComparisonFilter(new CommandParameter(Constant.Value), new CommandParameter(right.Constant.Value), op);
            }

            return right.CompareTo(this, ComparisonFilter.OppositeOperators[op]);
        }

        Comparable ??= Member as IComparableMember;
        if (Comparable != null && right.Constant != null)
        {
            return Comparable.CreateComparison(op, right.Constant);
        }

        if (Member == null)
        {
            throw new BadLinqExpressionException(
                $"Unsupported binary value expression in a Where() clause. {_expression} {op} {right._expression}");
        }

        if (right.Constant != null && Member is IComparableMember comparableMember)
        {
            return comparableMember.CreateComparison(op, right.Constant);
        }

        if (right.Member != null)
        {
            // TODO -- this will need to evaluate extra methods in the comparison. Looking for StringProp.ToLower() == "foo"
            // See https://github.com/JasperFx/marten/issues/2707
            return new MemberComparisonFilter(Member, right.Member, op);
        }

        if (right.HasConstant)
        {
            if (op == "=")
            {
                return new IsNullFilter(Member);
            }

            if (op == "!=")
            {
                return new IsNotNullFilter(Member);
            }
        }


        throw new BadLinqExpressionException("Unsupported binary value expression in a Where() clause");
    }




    protected override Expression VisitBinary(BinaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Modulo:
                Comparable = new ModuloOperator(node, _queryableMembers);
                return null;

            case ExpressionType.ArrayIndex:
                var index = (int)node.Right.ReduceToConstant().Value;
                Members.Insert(0, new ArrayIndexMember(index));

                if (node.Left is MemberExpression m)
                {
                    return VisitMember(m);
                }

                return base.VisitBinary(node);

            case ExpressionType.Equal:
                var left = new SimpleExpression(_queryableMembers, node.Left);
                var right = new SimpleExpression(_queryableMembers, node.Right);
                var filter = left.CompareTo(right, "=");
                Filters.Add(filter);

                return null;

            default:
                throw new BadLinqExpressionException(
                    $"Unsupported nested operator '{node.NodeType}' as an operand in a binary expression");
        }
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Not:
                var simple = new SimpleExpression(_queryableMembers, node.Operand);
                if (simple.Member is IComparableMember cm)
                {
                    Comparable = new NotMember(cm);
                }

                return null;

            case ExpressionType.Convert:
                if (node.Operand is ConstantExpression c)
                {
                    HasConstant = true;
                    Constant = c;
                }

                if (node.Operand is NewExpression)
                {
                    HasConstant = true;
                    Constant = Expression.Constant(node.Operand.Value());
                }
                else
                {
                    Visit(node.Operand);
                }


                return null;

            case ExpressionType.ArrayLength:
                Visit(node.Operand);
                Members.Add(LinqConstants.ArrayLength);

                return null;
        }

        throw new BadLinqExpressionException(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Cannot use TryParseConstant
        if (node.IsCompilableExpression())
        {
            Constant = _expression.ReduceToConstant();
            HasConstant = true;
            return null;
        }

        Members.Insert(0, node.Member);

        if (node.Expression is ParameterExpression)
        {
            FoundParameterAtStart = true;

            // Gotta keep visiting to get at possible ! operators
            Visit(node.Expression);
            return null;
        }

        Visit(node.Expression);
        return null;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        FoundParameterAtStart = true;
        return base.VisitParameter(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // TODO -- add new IQueryableMember.TryResolveMemberForMethod(node.Method). See https://github.com/JasperFx/marten/issues/2707
        if (node.Method.Name == "Count" && node.Method.DeclaringType == typeof(Enumerable))
        {
            if (node.Arguments.Count == 1)
            {
                Members.Insert(0, LinqConstants.ArrayLength);

                var finder = new MemberFinder();
                finder.Visit(node.Arguments[0]);

                FoundParameterAtStart = finder.FoundParameterAtFront;
                Members = finder.Members.Concat(Members).ToList();
                return null;
            }

            var collection = (ICollectionMember)_queryableMembers.MemberFor(node.Arguments[0]);

            Comparable = collection.ParseComparableForCount(node.Arguments.Last());
            return null;
        }

        if (node.Method.Name == "get_Item" && node.Method.DeclaringType.Closes(typeof(IDictionary<,>)))
        {
            var dictMember = (IDictionaryMember)_queryableMembers.MemberFor(node.Object);
            var key = node.Arguments[0].Value();
            Member = dictMember.MemberForKey(key);
            return null;
        }

        if (node.Object == null)
        {
            HasConstant = true;
            Constant = node.ReduceToConstant();
            return null;
        }

        Members.Insert(0, node.Method);

        foreach (var argument in node.Arguments) Visit(argument);

        if (node.Object != null)
        {
            Visit(node.Object);
        }

        return null;
    }

    public ISqlFragment FindValueFragment()
    {
        if (Member != null)
        {
            return Member;
        }

        if (HasConstant)
        {
            return new CommandParameter(Constant.Value);
        }

        throw new BadLinqExpressionException(
            $"$Simple expression '{_expression}' does not refer to either a simple queryable member or a constant value");
    }
}
