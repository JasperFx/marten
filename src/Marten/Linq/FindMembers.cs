using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class FindMembers : RelinqExpressionVisitor
    {
        public readonly IList<MemberInfo> Members = new List<MemberInfo>();

        protected override Expression VisitMember(MemberExpression node)
        {
            Members.Insert(0, node.Member);

            return base.VisitMember(node);
        }
    }
}