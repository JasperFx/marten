using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Includes;

namespace Marten.Linq.Parsing
{
    public partial class LinqHandlerBuilder
    {
        public class IncludeVisitor: ExpressionVisitor
        {
            public readonly IList<IIncludePlan> Includes;

            public IncludeVisitor(List<IIncludePlan> includes)
            {
                Includes = includes;
            }

            protected override Expression VisitNew(NewExpression node)
            {
                return null;
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                return null;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name == nameof(QueryableExtensions.IncludePlan))
                {
                    return VisitIncludePlan(node);
                }

                return null;
            }

            private Expression VisitIncludePlan(MethodCallExpression node)
            {
                if (node.Arguments[1].Value() is IIncludePlan include)
                {
                    Includes.Add(include);
                }

                var remainderExpression = node.Arguments[0];
                if (remainderExpression.CanReduce)
                {
                    return Visit(remainderExpression);
                }
                else if (remainderExpression is MethodCallExpression c)
                {
                    return VisitMethodCall(c);
                }

                return null;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                return null;
            }

            protected override Expression VisitMemberInit(MemberInitExpression node)
            {
                return null;
            }
        }
    }
}
