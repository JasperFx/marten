using System;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Methods;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

public static class LinqExtensions
{
    /// <summary>
    /// LINQ filter to select only a specified set of event types
    /// </summary>
    /// <param name="e"></param>
    /// <param name="types"></param>
    /// <returns></returns>
    public static bool EventTypesAre(this IEvent e, params Type[] types)
    {
        return e.Data.GetType().IsOneOf(types);
    }
}

internal class EventTypesAreParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(LinqExtensions.EventTypesAre) && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var types = (Type[])expression.Arguments.Last().Value();
        var typeNames = types.Select(x => options.Events.As<EventGraph>().EventMappingFor(x).EventTypeName).ToArray();

        var queryableMember = memberCollection.MemberFor(nameof(IEvent.EventTypeName));
        return new IsOneOfFilter(queryableMember, new CommandParameter(typeNames));
    }
}
