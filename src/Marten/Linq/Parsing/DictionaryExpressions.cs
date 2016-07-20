using System;
using System.Linq.Expressions;
using Marten.Schema;
using System.Reflection;
using System.Collections.Generic;

namespace Marten.Linq.Parsing
{
    public class DictionaryExpressions : IMethodCallParser
    {
        static bool IsCollectionContainsWithStringKey(MethodInfo m) => 
                m.Name == "Contains" 
            && m.DeclaringType.IsConstructedGenericType 
            && m.DeclaringType.GetGenericTypeDefinition() == typeof(ICollection<>)
            && m.DeclaringType.GenericTypeArguments[0].IsConstructedGenericType
            && m.DeclaringType.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            && m.DeclaringType.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(string);

        static bool IsDictionaryContainsKey(MethodInfo m) =>
               m.Name == "ContainsKey"
            && m.DeclaringType.IsConstructedGenericType
            && m.DeclaringType.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            && m.DeclaringType.GenericTypeArguments[0] == typeof(string);

        public bool Matches(MethodCallExpression expression)
        {
            return IsCollectionContainsWithStringKey(expression.Method)
                || IsDictionaryContainsKey(expression.Method);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var finder = new FindMembers();
            finder.Visit(expression);
            var members = finder.Members;
            var fieldlocator = mapping.FieldFor(members).SqlLocator;

            if (IsCollectionContainsWithStringKey(expression.Method))
            {
                return QueryFromICollectionContains(expression, fieldlocator, serializer);
            }
            else if (IsDictionaryContainsKey(expression.Method))
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
            var kvp = constant.Value; // is kvp<string, unknown>
            var kvpType = kvp.GetType();
            var key = kvpType.GetProperty("Key").GetValue(kvp);
            var value = kvpType.GetProperty("Value").GetValue(kvp);
            var dictType = typeof(Dictionary<,>).MakeGenericType(kvpType.GenericTypeArguments[0], kvpType.GenericTypeArguments[1]);
            var dict = dictType.GetConstructors()[0].Invoke(null);
            dictType.GetMethod("Add").Invoke(dict, new[] { key, value });
            var json = serializer.ToJson(dict);
            return new CustomizableWhereFragment($"{fieldPath} @> ?", "?", Tuple.Create<object, NpgsqlTypes.NpgsqlDbType?>(json, NpgsqlTypes.NpgsqlDbType.Jsonb));
        }
    }
}
