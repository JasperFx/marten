using System;
using System.Linq.Expressions;
using Marten.Schema;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Linq.Parsing
{
    public class DictionaryExpressions : IMethodCallParser
    {
        static readonly MethodInfo ICollectionKVPStringStringContains = typeof(ICollection<KeyValuePair<string, string>>).GetMethod("Contains");
        static readonly MethodInfo IDictionaryStringStringContainsKey = typeof(IDictionary<string, string>).GetMethod("ContainsKey");
        static readonly MethodInfo IEnumerableKVPStringStringContains = typeof(IEnumerable<KeyValuePair<string, string>>).GetMethod("Contains");
        public bool Matches(MethodCallExpression expression)
        {
            return 
                expression.Method == ICollectionKVPStringStringContains 
                || expression.Method == IDictionaryStringStringContainsKey 
                || expression.Method == IEnumerableKVPStringStringContains;
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
            else if (expression.Method == IEnumerableKVPStringStringContains)
            {
                return QueryFromIEnumerableContains(expression, fieldlocator, serializer);
            }
            else throw new NotImplementedException("Could not understand the format of the dictionary access");
        }

        static IWhereFragment QueryFromIEnumerableContains(MethodCallExpression expression, string fieldlocator, ISerializer serializer)
        {
            throw new NotImplementedException();
        }

        static IWhereFragment QueryFromDictionaryContainsKey(MethodCallExpression expression, string fieldLocator)
        {
            var key = (string)expression.Arguments[0].Value();
            return new WhereFragment($"{fieldLocator} ? ?", key);
        }

        static IWhereFragment QueryFromICollectionContains(MethodCallExpression expression, string fieldPath, ISerializer serializer)
        {
            var constant = expression.Arguments[0] as ConstantExpression;
            var kvp = (KeyValuePair<string, string>)constant.Value;
            var dict = serializer.ToJson(new Dictionary<string, string> { { kvp.Key, kvp.Value } });
            return new WhereFragment($"{fieldPath} @> ?", dict);
        }
    }
}
