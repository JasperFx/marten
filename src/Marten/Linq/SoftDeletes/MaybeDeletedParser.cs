using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

internal class MaybeDeletedParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.MaybeDeleted));

    private static readonly WhereFragment _whereFragment = new($"d.{SchemaConstants.DeletedColumn} is not null");

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method == _method;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        options.AssertDocumentTypeIsSoftDeleted(expression.Arguments[0].Type);

        return _whereFragment;
    }
}
