using System.Linq.Expressions;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ModuloOperator: IComparableMember, ISqlFragment
{
    private readonly ISqlFragment _left;
    private readonly ISqlFragment _right;
    private string _op;
    private CommandParameter _value;

    public ModuloOperator(BinaryExpression expression, IQueryableMemberCollection members)
    {
        _left = analyze(expression.Left, members);
        _right = analyze(expression.Right, members);
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression value)
    {
        _op = " " + op + " ";
        _value = new CommandParameter(value);

        return this;
    }

    public void Apply(CommandBuilder builder)
    {
        _left.Apply(builder);
        builder.Append(" % ");
        _right.Apply(builder);
        builder.Append(_op);
        _value.Apply(builder);
    }

    public bool Contains(string sqlText)
    {
        return false;
    }

    private ISqlFragment analyze(Expression expression, IQueryableMemberCollection members)
    {
        if (expression.TryToParseConstant(out var constant))
        {
            return new CommandParameter(constant.Value());
        }

        return members.MemberFor(expression);
    }
}
