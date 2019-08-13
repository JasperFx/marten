using System;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class ChildCollectionWhereVisitor: RelinqExpressionVisitor
    {
        public static readonly Type[] ValidOperators = new[] { typeof(AnyResultOperator), typeof(ContainsResultOperator) };

        private readonly ISerializer _serializer;
        private readonly SubQueryExpression _expression;
        private readonly Action<IWhereFragment> _registerFilter;
        private readonly QueryModel _query;
        private readonly IQueryableDocument _mapping;

        public ChildCollectionWhereVisitor(ISerializer serializer, SubQueryExpression expression, Action<IWhereFragment> registerFilter) : this(serializer, expression, registerFilter, null)
        {
        }

        public ChildCollectionWhereVisitor(ISerializer serializer, SubQueryExpression expression, Action<IWhereFragment> registerFilter, IQueryableDocument mapping)
        {
            _serializer = serializer;
            _expression = expression;
            _query = expression.QueryModel;
            _registerFilter = registerFilter;
            _mapping = mapping;
        }

        public void Parse()
        {
            var invalidOperators = _query.ResultOperators.Where(x => !ValidOperators.Contains(x.GetType()))
                .ToArray();

            if (invalidOperators.Any())
            {
                var names = invalidOperators.Select(x => x.GetType().Name).Join(", ");
                throw new NotSupportedException($"Marten does not yet support {names} operators in child collection queries");
            }

            var members = FindMembers.Determine(_query.MainFromClause.FromExpression);
            var queryType = _query.SourceType();
            var isPrimitive = TypeMappings.HasTypeMapping(queryType);

            Visit(_expression);

            // Simple types

            if (isPrimitive)
            {
                var contains = _query.ResultOperators.OfType<ContainsResultOperator>().FirstOrDefault();
                if (contains != null)
                {
                    var @where = ContainmentWhereFragment.SimpleArrayContains(members, _serializer, _query.MainFromClause.FromExpression, contains.Item.Value());
                    _registerFilter(@where);

                    return;
                }
            }

            if (_query.ResultOperators.Any(x => x is AnyResultOperator))
            {
                // Any() without predicate
                if (!_query.BodyClauses.Any())
                {
                    var @where_any_nopredicate = new CollectionAnyNoPredicateWhereFragment(members, _expression);

                    _registerFilter(@where_any_nopredicate);

                    return;
                }

                var @where = new CollectionAnyContainmentWhereFragment(members, _serializer, _expression, _mapping);
                _registerFilter(@where);
            }
        }

        public override Expression Visit(Expression node)
        {
            return base.Visit(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            //throw new NotSupportedException($"Marten does not yet support SubQuery searches with the {node.Method.DeclaringType.FullName}.{node.Method.Name} method");

            return base.VisitMethodCall(node);
        }
    }
}
