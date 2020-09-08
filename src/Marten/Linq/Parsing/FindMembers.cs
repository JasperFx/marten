using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

        public static MemberInfo[] Determine(Expression expression)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            return visitor.Members.ToArray();
        }
    }
}
