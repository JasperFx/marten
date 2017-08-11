using System.Linq.Expressions;

namespace Marten.Linq.Parsing
{
	public sealed class StringNotContains : StringContains
	{
		protected override string GetOperator(MethodCallExpression expression)
		{
			return $"NOT {base.GetOperator(expression)}";
		}
	}
}