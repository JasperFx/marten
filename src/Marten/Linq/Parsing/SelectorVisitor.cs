using System;
using System.Linq.Expressions;
using LamarCodeGeneration;

namespace Marten.Linq.Parsing
{
    internal partial class LinqHandlerBuilder
    {
        public class SelectorVisitor: ExpressionVisitor
        {
            private readonly LinqHandlerBuilder _parent;

            public SelectorVisitor(LinqHandlerBuilder parent)
            {
                _parent = parent;
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                _parent.CurrentStatement.ToScalar(node);
                return null;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                _parent.CurrentStatement.ToScalar(node);
                return null;
            }

            protected override Expression VisitMemberInit(MemberInitExpression node)
            {
                _parent.CurrentStatement.ToSelectTransform(node);
                return null;
            }

            protected override Expression VisitNew(NewExpression node)
            {
                _parent.CurrentStatement.ToSelectTransform(node);
                return null;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                bool matched = false;
                foreach (var matcher in _methodMatchers)
                {
                    if (matcher.TryMatch(node, this, out var op))
                    {
                        _parent.AddResultOperator(op, null); // SHOULD be impossible to get an Include operator here.
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    var method = node.Method;
                    throw new NotSupportedException($"Marten does not (yet) support the {method.DeclaringType.FullNameInCode()}.{method.Name}() method as a Linq selector");
                }

                return null;
            }


        }
    }
}
