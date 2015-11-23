using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Marten.Schema;
using Newtonsoft.Json;
using Npgsql;

namespace Marten.Linq
{
    public class ContainmentWhereFragment : IWhereFragment
    {
        private readonly static JsonSerializer _serializer = new JsonSerializer {TypeNameHandling = TypeNameHandling.None, DateFormatHandling = DateFormatHandling.IsoDateFormat};
        private readonly IDictionary<string, object> _dictionary = new Dictionary<string, object>(); 

        public ContainmentWhereFragment(DocumentMapping mapping, BinaryExpression binary)
        {
            

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
                _dictionary.Add(member.Name, MartenExpressionParser.Value(binary.Right));


            }
            
        }

        public string ToSql(NpgsqlCommand command)
        {
            var stringWriter = new StringWriter();
            _serializer.Serialize(new JsonTextWriter(stringWriter), _dictionary);

            return  $"data @> '{stringWriter}'";
        }
    }
}