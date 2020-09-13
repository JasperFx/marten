using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Exceptions;
using Marten.Linq.QueryHandlers;
using Newtonsoft.Json;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Marten.Linq.Parsing
{
    public class FindMembers: RelinqExpressionVisitor
    {
        public static MemberInfo Member<T>(Expression<Func<T, object>> expression)
        {
            var finder = new FindMembers();
            finder.Visit(expression);

            return finder.Members.LastOrDefault();
        }

        public readonly IList<MemberInfo> Members = new List<MemberInfo>();

        protected override Expression VisitMember(MemberExpression node)
        {
            Members.Insert(0, node.Member);

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Count" && node.Method.ReturnType == typeof(int))
            {
                Members.Insert(0, LinqConstants.ArrayLength);
            }

            return base.VisitMethodCall(node);
        }

        protected sealed override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.ArrayLength)
            {
                Members.Insert(0, LinqConstants.ArrayLength);
            }

            return base.VisitUnary(node);
        }

        public static MemberInfo[] Determine(Expression expression)
        {
            var visitor = new FindMembers();

            if (expression is SubQueryExpression subquery)
            {
                visitor.Visit(subquery.QueryModel.MainFromClause.FromExpression);
                if (subquery.QueryModel.ResultOperators.FirstOrDefault() is CountResultOperator)
                {
                    visitor.Members.Add(LinqConstants.ArrayLength);
                }
                else
                {
                    throw new BadLinqExpressionException($"FindMembers does not understand expression '{expression}'");
                }
            }
            else
            {
                visitor.Visit(expression);
            }




            return visitor.Members.ToArray();
        }
    }
}
