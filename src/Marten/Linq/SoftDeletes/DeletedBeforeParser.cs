using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

internal class DeletedBeforeParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.DeletedBefore));

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _method);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var documentType = memberCollection as DocumentQueryableMemberCollection;
        if (documentType == null)
        {
            throw new BadLinqExpressionException($"{_method.Name} can only be used to query against documents");
        }

        options.AssertDocumentTypeIsSoftDeleted(expression.Arguments[0].Type);

        var time = expression.Arguments.Last().Value().As<DateTimeOffset>();

        return new WhereFragment($"d.{SchemaConstants.DeletedColumn} and d.{SchemaConstants.DeletedAtColumn} < ?",
            time);
    }
}
