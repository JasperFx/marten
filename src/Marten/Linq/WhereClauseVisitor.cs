using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
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

            protected override Expression VisitMethodCall(MethodCallExpression expression)
            {
                var parser = _parent._options.Linq.MethodCallParsers.FirstOrDefault(x => x.Matches(expression)) 
                    ?? _parsers.FirstOrDefault(x => x.Matches(expression));

                if (parser != null)
                {
                    var @where = parser.Parse(_mapping, _parent._serializer, expression);
                    _register.Peek()(@where);

                    return null;
                }


                throw new NotSupportedException($"Marten does not (yet) support Linq queries using the {expression.Method.DeclaringType.FullName}.{expression.Method.Name}() method");
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                switch (node.NodeType)
                {
                    case ExpressionType.Not:
                        var visitor = new NotVisitor(this, _register.Peek());
                        visitor.Visit(node);

                        return null;
                }


                return base.VisitUnary(node);
            }

            public class NotVisitor : RelinqExpressionVisitor
            {
                private readonly WhereClauseVisitor _parent;
                private readonly Action<IWhereFragment> _callback;

                public NotVisitor(WhereClauseVisitor parent, Action<IWhereFragment> callback)
                {
                    _parent = parent;
                    _callback = callback;
                }

                protected override Expression VisitMember(MemberExpression expression)
                {
                    if (expression.Type == typeof (bool))
                    {
                        var locator = _parent._mapping.JsonLocator(expression);
                        var @where = new WhereFragment($"({locator})::Boolean = False");
                        _callback(@where);
                    }

                    return base.VisitMember(expression);
                }

                protected override Expression VisitBinary(BinaryExpression expression)
                {
                    if (expression.Type == typeof (bool) && expression.NodeType == ExpressionType.NotEqual)
                    {
                        var binaryExpression = expression.As<BinaryExpression>();
                        var locator = _parent._mapping.JsonLocator(binaryExpression.Left);
                        if (binaryExpression.Right.NodeType == ExpressionType.Constant &&
                            binaryExpression.Right.As<ConstantExpression>().Value == null)
                        {
                            var @where = new WhereFragment($"({locator}) IS NULL");
                            _callback(@where);
                        }
                    }

                    return base.VisitBinary(expression);
                }
            }

            protected override Expression VisitSubQuery(SubQueryExpression expression)
            {
                var queryType = expression.QueryModel.MainFromClause.ItemType;

                // Simple types
                if (TypeMappings.HasTypeMapping(queryType))
                {
                    var contains = expression.QueryModel.ResultOperators.OfType<ContainsResultOperator>().FirstOrDefault();
                    if (contains != null)
                    {
                        var @where = ContainmentWhereFragment.SimpleArrayContains(_parent._serializer, expression.QueryModel.MainFromClause.FromExpression, contains.Item.Value());
                        _register.Peek()(@where);

                        return null;
                    }
                }

                if (expression.QueryModel.ResultOperators.Any(x => x is AnyResultOperator))
                {
                    var @where = new CollectionAnyContainmentWhereFragment(_parent._serializer, expression);

                    _register.Peek()(@where);

                    return null;
                }


                return base.VisitSubQuery(expression);
            }

            protected override Expression VisitMember(MemberExpression expression)
            {
                if (expression.Type == typeof (bool))
                {
                    var locator = _mapping.JsonLocator(expression);
                    var @where = new WhereFragment("{0} = True".ToFormat(locator), true);
                    _register.Peek()(@where);
                    return null;
                }

                return base.VisitMember(expression);
            }
        }
    }
}