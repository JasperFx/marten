using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class DictionaryContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var m = expression.Method;
        return m.Name == nameof(IDictionary<string, string>.Contains)
               && m.DeclaringType != null && m.DeclaringType.IsConstructedGenericType
               && m.DeclaringType.GetGenericTypeDefinition() == typeof(ICollection<>)
               && m.DeclaringType.GenericTypeArguments[0].IsConstructedGenericType
               && m.DeclaringType.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
               && (m.DeclaringType.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(string)
                   || m.DeclaringType.GenericTypeArguments[0].GenericTypeArguments[0].IsValueType);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var member = memberCollection.MemberFor(expression.Arguments.First());
        var constant = expression.Arguments.Last().ReduceToConstant();

        return new ContainmentWhereFilter(member, constant, options.Serializer());
    }
}
