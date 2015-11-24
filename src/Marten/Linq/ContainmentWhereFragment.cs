using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Schema;
using Newtonsoft.Json;
using Npgsql;

namespace Marten.Linq
{
    public class ContainmentWhereFragment : IWhereFragment
    {
        private readonly ISerializer _serializer;
        private readonly IDictionary<string, object> _dictionary = new Dictionary<string, object>(); 

        public ContainmentWhereFragment(ISerializer serializer, BinaryExpression binary)
        {
            _serializer = serializer;


            var visitor = new FindMembers();
            visitor.Visit(binary.Left);

            var members = visitor.Members;

            if (members.Count > 1)
            {
                throw new NotSupportedException("Not sure that the containment operator can do deep searches yet");
            }
            else
            {
                var member = members.Single();
                var value = MartenExpressionParser.Value(binary.Right);
                _dictionary.Add(member.Name, value);


            }
            
        }

        public string ToSql(NpgsqlCommand command)
        {
            var json = _serializer.ToJson(_dictionary);

            return  $"data @> '{json}'";
        }
    }
}