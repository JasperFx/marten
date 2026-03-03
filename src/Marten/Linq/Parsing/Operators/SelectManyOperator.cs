#nullable enable
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

        // If this SelectMany flattens a GroupJoin, store the full expression
        // so that GroupJoinOperator / CompileGroupJoin can access the collection selector
        // (needed to detect DefaultIfEmpty for LEFT JOIN)
        if (expression.Arguments[0] is MethodCallExpression source &&
            source.Method.Name == "GroupJoin")
        {
            usage.SelectManyCallExpression = expression;
        }
    }
}
