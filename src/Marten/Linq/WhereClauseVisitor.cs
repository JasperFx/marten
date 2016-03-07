using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Schema;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public partial class MartenExpressionParser
    {

        public class WhereClauseVisitor : RelinqExpressionVisitor
        {
            private readonly MartenExpressionParser _parent;
            private readonly IDocumentMapping _mapping;
            private readonly Stack<Action<IWhereFragment>> _register = new Stack<Action<IWhereFragment>>();
            private IWhereFragment _top;

            public WhereClauseVisitor(MartenExpressionParser parent, IDocumentMapping mapping)
            {
                _parent = parent;
                _mapping = mapping;
                _register.Push(x => _top = x);
            }

            public IWhereFragment ToWhereFragment()
            {
                return _top;
            }

            protected override Expression VisitBinary(BinaryExpression binary)
            {
                if (_operators.ContainsKey(binary.NodeType))
                {
                    var fragment = _parent.buildSimpleWhereClause(_mapping, binary);
                    _register.Peek()(fragment);

                    return null;
                }

                if (binary.NodeType == ExpressionType.AndAlso || binary.NodeType == ExpressionType.OrElse)
                {
                    var separator = binary.NodeType == ExpressionType.AndAlso
                        ? "and"
                        : "or";

                    var compound = new CompoundWhereFragment(separator);
                    _register.Peek()(compound);
                    _register.Push(child => compound.Add(child));

                    Visit(binary.Left);
                    Visit(binary.Right);


                    return null;
                }


                throw new NotSupportedException($"Marten does not support the BinaryExpression {binary} (yet).");
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                return base.VisitMethodCall(node);
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                return base.VisitUnary(node);
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                return base.VisitSubQuery(expression);
            }
        }
    }
}