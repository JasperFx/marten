using System.Linq;
using System.Linq.Expressions;

namespace Marten.Linq.Parsing.Operators;

public class SelectManyOperator: LinqOperator
{
    public SelectManyOperator(): base("SelectMany")
    {
    }

    public override void Apply(ILinqQuery query, MethodCallExpression expression)
    {
        var usage = query.StartNewCollectionUsageFor(expression);
        usage.SelectMany = expression.Arguments.Last();
    }
}
