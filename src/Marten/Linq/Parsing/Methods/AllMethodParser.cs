#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Parsing.Methods;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
internal class AllMethodParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];

        return expression.Method.Name == LinqConstants.ALL &&
               typeMatches(member.Type);
    }


    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var member = memberCollection.MemberFor(expression.Arguments[0]);

        if (member is ICollectionMember cm)
        {
            return cm.ParseWhereForAll(expression, options);
        }

        throw new BadLinqExpressionException("Marten does not know how to handle expression " + expression);
    }

    private static bool typeMatches(Type type)
    {
        return type.IsGenericEnumerable() || type.Closes(typeof(IReadOnlyList<>)) || type.IsArray;
    }
}
