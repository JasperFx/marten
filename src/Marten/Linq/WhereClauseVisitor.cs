using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public partial class MartenExpressionParser
    {
        public class WhereClauseVisitor: RelinqExpressionVisitor
        {
            private readonly IQueryableDocument _mapping;
            private readonly MartenExpressionParser _parent;
            private readonly Stack<Action<IWhereFragment>> _register = new Stack<Action<IWhereFragment>>();
            private IWhereFragment _top;

            public WhereClauseVisitor(MartenExpressionParser parent, IQueryableDocument mapping)
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

                if ((binary.NodeType == ExpressionType.AndAlso) || (binary.NodeType == ExpressionType.OrElse))
                {
                    var separator = binary.NodeType == ExpressionType.AndAlso
                        ? "and"
                        : "or";

                    var compound = new CompoundWhereFragment(separator);
                    _register.Peek()(compound);
                    _register.Push(child => compound.Add(child));

                    Visit(binary.Left);
                    Visit(binary.Right);

                    _register.Pop();

                    return null;
                }

                throw new NotSupportedException($"Marten does not support the BinaryExpression {binary} (yet).");
            }

            protected override Expression VisitMethodCall(MethodCallExpression expression)
            {
                var parser = _parent.FindMethodParser(expression);

                if (parser == null)
                {
                    throw new NotSupportedException(
                        $"Marten does not (yet) support Linq queries using the {expression.Method.DeclaringType.FullName}.{expression.Method.Name}() method");
                }

                var where = parser.Parse(_mapping, _parent._serializer, expression);
                _register.Peek()(@where);

                // ReSharper disable once AssignNullToNotNullAttribute
                return null;

            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                switch (node.NodeType)
                {
                    case ExpressionType.Not:
                        if (node.Operand is SubQueryExpression)
                        {
                            var nested = new WhereClauseVisitor(_parent, _mapping);
                            nested.Visit(node.Operand);

                            var @where = new NotWhereFragment(nested.ToWhereFragment());
                            _register.Peek()(@where);
                        }
                        else
                        {
                            var visitor = new NotVisitor(this, _mapping, _register.Peek(), _parent._serializer);
                            visitor.Visit(node);
                        }

                        return null;
                }

                return base.VisitUnary(node);
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if ((node.Type == typeof(bool)))
                    _register.Peek()(new WhereFragment(node.Value.ToString().ToLower()));
                return base.VisitConstant(node);
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                Action<IWhereFragment> register = w => _register.Peek()(w);

                var visitor = new ChildCollectionWhereVisitor(_parent._serializer, expression, register, _mapping);
                visitor.Parse();

                return null;
            }

            protected override Expression VisitMember(MemberExpression expression)
            {
                if (expression.Type == typeof(bool))
                {
                    var locator = _mapping.JsonLocator(expression);
                    var where = new WhereFragment("{0} = True".ToFormat(locator), true);
                    _register.Peek()(where);
                    return null;
                }

                return base.VisitMember(expression);
            }
        }
    }
}
