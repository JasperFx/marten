#nullable enable
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

internal class IsDeletedParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.IsDeleted))!;

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method == _method;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var argument = expression.Arguments[0];
        var type = argument is UnaryExpression u ? u.Operand.Type : argument.Type;
        options.AssertDocumentTypeIsSoftDeleted(type);
        return IsDeletedFilter.Instance;
    }

}

internal class IsDeletedFilter: ISoftDeletedFilter, IReversibleWhereFragment
{
    public static readonly IsDeletedFilter Instance = new();

    private IsDeletedFilter()
    {

    }

    private static readonly string _sql = $"d.{SchemaConstants.DeletedColumn} = True";

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public ISqlFragment Reverse()
    {
        return IsNotDeletedFilter.Instance;
    }
}

internal class IsNotDeletedFilter: ISoftDeletedFilter, IReversibleWhereFragment
{
    public static readonly IsNotDeletedFilter Instance = new();

    private IsNotDeletedFilter()
    {

    }

    private static readonly string _sql = $"d.{SchemaConstants.DeletedColumn} = False";

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append(_sql);
    }

    public ISqlFragment Reverse()
    {
        return IsDeletedFilter.Instance;
    }
}
