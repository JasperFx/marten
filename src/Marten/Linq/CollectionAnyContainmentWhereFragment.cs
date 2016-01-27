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

            var search = new Dictionary<string, object>();
            wheres.OfType<BinaryExpression>()
                .Each(x =>
                {
                    gatherSearch(x, search);
                    
                });

            var visitor = new FindMembers();
            visitor.Visit(_expression.QueryModel.MainFromClause.FromExpression);

            var members = visitor.Members;
            var dictionary = new Dictionary<string, object>();

            if (members.Count == 1)
            {
                dictionary.Add(members.Single().Name, new[] {search});
            }
            else
            {
                throw new NotImplementedException();
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