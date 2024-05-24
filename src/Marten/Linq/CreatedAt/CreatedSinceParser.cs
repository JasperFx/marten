using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.CreatedAt;

public class CreatedSinceParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(CreatedAtExtensions).GetMethod(nameof(CreatedAtExtensions.CreatedSince));

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _method);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var time = expression.Arguments.Last().Value().As<DateTimeOffset>();

        return new WhereFragment($"d.{SchemaConstants.CreatedAtColumn} > ?", time);
    }
}
