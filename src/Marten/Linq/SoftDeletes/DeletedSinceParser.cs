using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

internal class DeletedSinceParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.DeletedSince));

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

        return new DeletedSinceFilter(time);
    }
}

internal class DeletedSinceFilter: ISoftDeletedFilter
{
    private readonly DateTimeOffset _time;

    private static readonly string _sql = $"d.{SchemaConstants.DeletedColumn} and d.{SchemaConstants.DeletedAtColumn} > ";

    public DeletedSinceFilter(DateTimeOffset time)
    {
        _time = time;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
        builder.AppendParameter(_time);
    }
}
