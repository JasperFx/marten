using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Npgsql;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Marten.Linq
{
    // TODO -- this is going to have to get redone
    public class CollectionAnyContainmentWhereFragment : IWhereFragment
    {
        private readonly ISerializer _serializer;
        private readonly SubQueryExpression _expression;


        public CollectionAnyContainmentWhereFragment(ISerializer serializer, SubQueryExpression expression)
        {
            _serializer = serializer;
            _expression = expression;
        }

        public string ToSql(NpgsqlCommand command)
        {
            var wheres = _expression
                .QueryModel
                .BodyClauses
                .OfType<WhereClause>()
                .Select(x => x.Predicate)
                .ToArray();

            if (!wheres.All(x => x is BinaryExpression))
            {
                throw new NotImplementedException();
            }


            var visitor = new FindMembers();
            visitor.Visit(_expression.QueryModel.MainFromClause.FromExpression);

            var members = visitor.Members;
            var binaryExpressions = wheres.OfType<BinaryExpression>().ToArray();
            var dictionary = new Dictionary<string, object>();

            // Are we querying directly againt the elements as you would for primitive types?
            if (binaryExpressions.All(x => x.Left is QuerySourceReferenceExpression && x.Right is ConstantExpression))
            {
                if (binaryExpressions.Any(x => x.NodeType != ExpressionType.Equal))
                {
                    throw new NotSupportedException("Only the equality operator is supported on Collection.Any(x => x) searches directly against the element");
                }

                var values = binaryExpressions.Select(x => MartenExpressionParser.Value(x.Right)).ToArray();
                if (members.Count == 1)
                {
                    dictionary.Add(members.Single().Name, values);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else
            {
                var search = new Dictionary<string, object>();
                binaryExpressions.Each(x => gatherSearch(x, search));


                if (members.Count == 1)
                {
                    dictionary.Add(members.Single().Name, new[] { search });
                }
                else
                {
                    throw new NotImplementedException();
                }
            }




            var json = _serializer.ToCleanJson(dictionary);

            return $"data @> '{json}'";
        }

        private static void gatherSearch(BinaryExpression x, Dictionary<string, object> search)
        {
            if (x.NodeType == ExpressionType.AndAlso)
            {
                if (x.Left is BinaryExpression) gatherSearch(x.Left.As<BinaryExpression>(), search);
                if (x.Right is BinaryExpression) gatherSearch(x.Right.As<BinaryExpression>(), search);
            }
            else if (x.NodeType == ExpressionType.Equal)
            {
                ContainmentWhereFragment.CreateDictionaryForSearch(x, search);
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }
}