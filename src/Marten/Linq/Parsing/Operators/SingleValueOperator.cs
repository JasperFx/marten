using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

internal class SingleValueOperator: LinqOperator
{
    private readonly bool _isMathOperator;

    public SingleValueOperator(SingleValueMode mode): base(mode.ToString())
    {
        Mode = mode;
        _isMathOperator = (int)mode > 10;
    }

    public SingleValueMode Mode { get; }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.CollectionUsageFor(expression);

        usage.SingleValueMode = Mode;

        if (expression.Arguments.Count > 1)
        {
            if (_isMathOperator)
            {
                usage.AddSelectClause(expression);
            }
            else
            {
                usage.AddWhereClause(expression);
            }
        }
    }
}
