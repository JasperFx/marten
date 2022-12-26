using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
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

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        return _whereFragment;
    }
}
