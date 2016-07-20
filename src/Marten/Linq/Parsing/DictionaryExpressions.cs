using System;
using System.Linq.Expressions;
using Marten.Schema;
using System.Reflection;
using System.Collections.Generic;

namespace Marten.Linq.Parsing
{
    public class DictionaryExpressions : IMethodCallParser
    {
        static readonly MethodInfo ICollectionKVPStringStringContains = typeof(ICollection<KeyValuePair<string, string>>).GetMethod("Contains");
        static readonly MethodInfo IDictionaryStringStringContainsKey = typeof(IDictionary<string, string>).GetMethod("ContainsKey");

        public bool Matches(MethodCallExpression expression)
        {
            return
                expression.Method == ICollectionKVPStringStringContains
                || expression.Method == IDictionaryStringStringContainsKey;
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var finder = new FindMembers();
            finder.Visit(expression);
            var members = finder.Members;
            var fieldlocator = mapping.FieldFor(members).SqlLocator;

            if (expression.Method == ICollectionKVPStringStringContains)
            {
                return QueryFromICollectionContains(expression, fieldlocator, serializer);
            }
            else if (expression.Method == IDictionaryStringStringContainsKey)
            {
                return QueryFromDictionaryContainsKey(expression, fieldlocator);
            }
            else throw new NotImplementedException("Could not understand the format of the dictionary access");
        }

        static IWhereFragment QueryFromDictionaryContainsKey(MethodCallExpression expression, string fieldLocator)
        {
            var key = (string)expression.Arguments[0].Value();
            // have to use different token here because we actually want the `?` character as the operator!
            return new CustomizableWhereFragment($"{fieldLocator} ? @1", "@1", Tuple.Create<object, NpgsqlTypes.NpgsqlDbType?>(key, NpgsqlTypes.NpgsqlDbType.Text)); 
        }

        static IWhereFragment QueryFromICollectionContains(MethodCallExpression expression, string fieldPath, ISerializer serializer)
        {
            var constant = expression.Arguments[0] as ConstantExpression;
            var kvp = (KeyValuePair<string, string>)constant.Value;
            var dict = serializer.ToJson(new Dictionary<string, string> { { kvp.Key, kvp.Value } });
            return new CustomizableWhereFragment($"{fieldPath} @> ?", "?", Tuple.Create<object, NpgsqlTypes.NpgsqlDbType?>(dict, NpgsqlTypes.NpgsqlDbType.Jsonb));
        }
    }
}
