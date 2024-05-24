using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Archiving;

internal class MaybeArchivedMethodCallParser: IMethodCallParser
{
    private static readonly MethodInfo _method =
        typeof(ArchivedEventExtensions).GetMethod(nameof(ArchivedEventExtensions.MaybeArchived));

    private static readonly ISqlFragment _whereFragment = new AllEventsFilter();


    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method == _method;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        return _whereFragment;
    }
}
