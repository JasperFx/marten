using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    public class ContainmentWhereFragment: ISqlFragment
    {
        private readonly IDictionary<string, object> _dictionary;
        private readonly string _wherePrefix;
        private readonly ISerializer _serializer;

        public ContainmentWhereFragment(ISerializer serializer, IDictionary<string, object> dictionary, string wherePrefix = null)
        {
            _serializer = serializer;
            _dictionary = dictionary;
            _wherePrefix = wherePrefix;
        }

        public ContainmentWhereFragment(ISerializer serializer, BinaryExpression binary, string wherePrefix = null)
            : this(serializer, new Dictionary<string, object>(), wherePrefix)
        {
            CreateDictionaryForSearch(binary, _dictionary, _serializer);
        }

        public void Apply(CommandBuilder builder)
        {
            var json = _serializer.ToCleanJson(_dictionary);

            builder.Append($"{_wherePrefix}d.data @> ");
            builder.AppendParameter(json, NpgsqlDbType.Jsonb);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }

        public static void CreateDictionaryForSearch(BinaryExpression binary, IDictionary<string, object> dict, ISerializer serializer)
        {
            var expressionValue = binary.Right.Value();
            var memberExpression = binary.Left;

            CreateDictionaryForSearch(dict, memberExpression, expressionValue, serializer);
        }

        public static void CreateDictionaryForSearch(IDictionary<string, object> dict, Expression memberExpression,
            object expressionValue, ISerializer serializer)
        {
            var visitor = new FindMembers();
            visitor.Visit(memberExpression);

            var members = visitor.Members;

            if (members.Count > 1)
            {
                var temp = new Dictionary<string, object>();
                var member = members.Last();
                var value = GetMemberValue(member, expressionValue, serializer.EnumStorage);

                temp.Add(member.Name, value);

                members.Reverse().Skip(1).Each(m => { temp = new Dictionary<string, object> { { m.Name, temp } }; });

                var topMemberName = members.First().Name;
                dict.Add(topMemberName, temp[topMemberName]);
            }
            else
            {
                var member = members.Single();
                var value = GetMemberValue(member, expressionValue, serializer.EnumStorage);

                dict.Add(member.Name, value);
            }
        }

        public static ISqlFragment SimpleArrayContains(MemberInfo[] members, ISerializer serializer, Expression @from, object value)
        {
            if (value != null)
            {
                var array = Array.CreateInstance(value.GetType(), 1);
                array.SetValue(value, 0);

                var dict = new Dictionary<string, object> { { members.Last().Name, array } };

                members.Reverse().Skip(1).Each(m => { dict = new Dictionary<string, object> { { m.Name, dict } }; });

                return new ContainmentWhereFragment(serializer, dict);
            }

            throw new NotSupportedException();
        }

        private static object GetMemberValue(MemberInfo member, object expressionValue, EnumStorage enumStorage)
        {
            var value = expressionValue;

            var memberType = member.GetMemberType();

            if (memberType.IsEnum && enumStorage == EnumStorage.AsString)
            {
                value = Enum.GetName(memberType, value);
            }

            return value;
        }
    }
}
