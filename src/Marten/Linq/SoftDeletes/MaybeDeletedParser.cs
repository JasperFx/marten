using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

internal class MaybeDeletedParser: IMethodCallParser, ISoftDeletedFilter
{
    private static readonly MethodInfo _method =
        typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.MaybeDeleted));


    private static readonly string _sql = $"d.{SchemaConstants.DeletedColumn} is not null";

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.GetGenericMethodDefinition() == _method;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        options.AssertDocumentTypeIsSoftDeleted(expression.Arguments[0].Type);

        return this;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_sql);
    }
}
