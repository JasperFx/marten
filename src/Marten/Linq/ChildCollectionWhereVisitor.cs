using System;
using System.Linq;
using Marten.Util;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public class ChildCollectionWhereVisitor
    {
        public static void Parse(ISerializer serializer, SubQueryExpression expression, Action<IWhereFragment> registerFilter)
        {
            var queryType = expression.QueryModel.MainFromClause.ItemType;

            // Simple types
            if (TypeMappings.HasTypeMapping(queryType))
            {
                var contains = expression.QueryModel.ResultOperators.OfType<ContainsResultOperator>().FirstOrDefault();
                if (contains != null)
                {
                    var @where = ContainmentWhereFragment.SimpleArrayContains(serializer, expression.QueryModel.MainFromClause.FromExpression, contains.Item.Value());
                    registerFilter(@where);

                    return;
                }
            }

            if (expression.QueryModel.ResultOperators.Any(x => x is AnyResultOperator))
            {
                // Any() without predicate
                if (!expression.QueryModel.BodyClauses.Any())
                {
                    var @where_any_nopredicate = new CollectionAnyNoPredicateWhereFragment(expression);

                    registerFilter(@where_any_nopredicate);

                    return;
                }

                var @where = new CollectionAnyContainmentWhereFragment(serializer, expression);
                registerFilter(@where);

            }
        }
    }
}