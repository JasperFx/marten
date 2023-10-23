using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.CreatedAt;

public class CreatedBeforeParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(CreatedAtExtensions).GetMethod(nameof(CreatedAtExtensions.CreatedBefore));

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _method);
    }

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        var time = expression.Arguments.Last().Value().As<DateTimeOffset>();

        return new WhereFragment($"d.{SchemaConstants.CreatedAtColumn} < ?", time);
    }
}
