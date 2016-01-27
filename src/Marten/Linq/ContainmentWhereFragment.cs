using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public class ContainmentWhereFragment : IWhereFragment
    {
        private readonly IDictionary<string, object> _dictionary;
        private readonly ISerializer _serializer;


        public ContainmentWhereFragment(ISerializer serializer, IDictionary<string, object> dictionary)
        {
            _serializer = serializer;
            _dictionary = dictionary;
        }

        public ContainmentWhereFragment(ISerializer serializer, BinaryExpression binary) : this(serializer, new Dictionary<string, object>())
        {
            CreateDictionaryForSearch(binary, _dictionary);
        }

        public static void CreateDictionaryForSearch(BinaryExpression binary, IDictionary<string, object> dict)
        {
            var visitor = new FindMembers();
            visitor.Visit(binary.Left);

            var members = visitor.Members;

            if (members.Count > 1)
            {
                var temp = new Dictionary<string, object>();
                var member = members.Last();
                var value = MartenExpressionParser.Value(binary.Right);
                temp.Add(member.Name, value);

                members.Reverse().Skip(1).Each(m => { temp = new Dictionary<string, object> {{m.Name, temp}}; });

                var topMemberName = members.First().Name;
                dict.Add(topMemberName, temp[topMemberName]);
            }
            else
            {
                var member = members.Single();
                var value = MartenExpressionParser.Value(binary.Right);
                dict.Add(member.Name, value);
            }

        }

        public string ToSql(NpgsqlCommand command)
        {
            var json = _serializer.ToCleanJson(_dictionary);

            return $"data @> '{json}'";
        }

        public static IWhereFragment SimpleArrayContains(ISerializer serializer, QueryModel queryModel,
            ContainsResultOperator contains)
        {
            var from = queryModel.MainFromClause.FromExpression;
            var visitor = new FindMembers();
            visitor.Visit(from);

            var members = visitor.Members;

            var constant = contains.Item as ConstantExpression;

            if (constant != null)
            {
                var array = Array.CreateInstance(constant.Type, 1);
                array.SetValue(constant.Value, 0);

                var dict = new Dictionary<string, object>();
                dict.Add(members.Last().Name, array);

                members.Reverse().Skip(1).Each(m => { dict = new Dictionary<string, object> {{m.Name, dict}}; });

                return new ContainmentWhereFragment(serializer, dict);
            }


            throw new NotSupportedException();
        }
    }
}