using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

public class WhereClauseParser: ExpressionVisitor
{
    private static readonly Dictionary<ExpressionType, string> _operators = new()
    {
        { ExpressionType.Equal, "=" },
        { ExpressionType.NotEqual, "!=" },
        { ExpressionType.GreaterThan, ">" },
        { ExpressionType.GreaterThanOrEqual, ">=" },
        { ExpressionType.LessThan, "<" },
        { ExpressionType.LessThanOrEqual, "<=" }
    };

    private readonly IQueryableMemberCollection _members;


    private readonly StoreOptions _options;
    private IWhereFragmentHolder _holder;

    public WhereClauseParser(StoreOptions options, IQueryableMemberCollection members,
        IWhereFragmentHolder holder)
    {
        _options = options;
        _members = members;
        _holder = holder;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var value = node.Value;
        if (value is bool b && b)
        {
            _holder.Register(new WhereFragment("1 = 1"));
        }

        return null;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Type == typeof(bool))
        {
            // Check if it's a literal field. See https://github.com/JasperFx/marten/issues/2850
            if (node.TryToParseConstant(out var constant))
            {
                _holder.Register(constant.Value.Equals(true) ? new WhereFragment("true") : new WhereFragment("false"));
                return null;
            }

            var field = _members.MemberFor(node);
            if (field is IBooleanField b)
            {
                _holder.Register(b.BuildIsTrueFragment());
            }
            else
            {
                _holder.Register(new BooleanFieldIsTrue(field));
            }

            return null;
        }

        return base.VisitMember(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var parser = _options.Linq.FindMethodParser(node);
        if (parser == null)
        {
            throw new NotSupportedException(
                $"Marten does not (yet) support Linq queries using the {node.Method.DeclaringType.FullName}.{node.Method.Name}() method");
        }

        var fragment = parser.Parse(_members, _options, node);
        _holder.Register(fragment);

        return null;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (_operators.TryGetValue(node.NodeType, out var op))
        {
            var left = new SimpleExpression(_members, node.Left);
            var right = new SimpleExpression(_members, node.Right);

            var fragment = left.CompareTo(right, op);

            _holder.Register(fragment);

            return null;
        }

        switch (node.NodeType)
        {
            case ExpressionType.AndAlso:
                buildCompoundWhereFragment(node, "and");
                break;

            case ExpressionType.OrElse:
                buildCompoundWhereFragment(node, "or");
                break;

            default:
                throw new BadLinqExpressionException(
                    $"Unsupported expression type '{node.NodeType}' in binary expression");
        }


        return null;
    }

    private void buildCompoundWhereFragment(BinaryExpression node, string separator)
    {
        var original = _holder;

        var compound = CompoundWhereFragment.For(separator);
        _holder.Register(compound);

        _holder = compound;

        Visit(node.Left);

        _holder = compound;

        Visit(node.Right);

        _holder = original;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            var original = _holder;

            if (original is IReversibleWhereFragment r)
            {
                _holder.Register(r.Reverse());
                return Visit(node.Operand);
            }

            _holder = new NotWhereFragment(original);
            var returnValue = Visit(node.Operand);

            _holder = original;

            return returnValue;
        }
        else if (node.NodeType == ExpressionType.OrElse)
        {
            Debug.WriteLine(node);
        }

        return null;
    }
}
