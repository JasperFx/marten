#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members.Dictionaries;

internal class DictionaryContainsKey: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var method = expression.Method;

        return method.Name == nameof(IDictionary<string, string>.ContainsKey)
               && method.DeclaringType != null && method.DeclaringType.IsConstructedGenericType
               && method.DeclaringType.Closes(typeof(IDictionary<,>))
               && (method.DeclaringType.GenericTypeArguments[0] == typeof(string)
                   || method.DeclaringType.GenericTypeArguments[0].IsValueType);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var member = memberCollection.MemberFor(expression.Object);
        var constant = expression.Arguments.Single().ReduceToConstant();

        return new DictionaryContainsKeyFilter((IDictionaryMember)member, options.Serializer(), constant);
    }
}
