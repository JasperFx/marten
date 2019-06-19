using System.Linq.Expressions;

namespace Marten.Linq.Parsing
{
    public class StringNotEndsWith: StringEndsWith
    {
        protected override string GetOperator(MethodCallExpression expression)
        {
            return $"NOT {base.GetOperator(expression)}";
        }
    }
}
