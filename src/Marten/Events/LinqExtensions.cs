using System;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.Dcb;
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

    /// <summary>
    /// LINQ filter over an event query (e.g. <c>session.Events.QueryAllRawEvents()</c>) that matches only
    /// events carrying the given DCB tag value. Composes into the same <c>Where()</c> as ordinary event
    /// predicates (timestamp, event type, stream), so one query can express "these events, matching these
    /// tags". <typeparamref name="TTag"/> must be a registered tag type (see <c>RegisterTagType&lt;TTag&gt;()</c>).
    /// AND-ing several <c>HasTag</c> calls with normal predicates is supported; for OR-across-tags or the
    /// richer event-type interplay, use the <c>EventTagQuery</c> builder with <c>QueryByTagsAsync</c> instead.
    /// This is a marker method recognized by the LINQ provider and cannot be invoked directly.
    /// </summary>
    public static bool HasTag<TTag>(this IEvent e, TTag value) where TTag : notnull
    {
        throw new NotSupportedException(
            "IEvent.HasTag<TTag>() is a marker method for LINQ event queries and cannot be invoked directly. Use it inside session.Events.QueryAllRawEvents().Where(...).");
    }
}

internal class HasTagParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(LinqExtensions.HasTag)
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var events = options.Events.As<EventGraph>();

        var tagType = expression.Method.GetGenericArguments()[0];
        var value = expression.Arguments.Last().Value()
                    ?? throw new ArgumentException("HasTag() requires a non-null tag value.", nameof(expression));

        var registration = events.FindTagType(tagType)
                           ?? throw new InvalidOperationException(
                               $"Tag type '{tagType.Name}' is not registered. Call RegisterTagType<{tagType.Name}>() first.");

        // HStore mode: all tags live in a single hstore column on mt_events; match with containment.
        if (events.DcbStorageMode == DcbStorageMode.HStore)
        {
            var stringValue = TagValueStringifier.Stringify(registration.ExtractValue(value));
            return new WhereFragment("d.tags @> hstore(?, ?)", registration.TableSuffix, stringValue);
        }

        // TagTables mode (default): correlate to the per-tag table via seq_id, mirroring the JOIN that
        // BuildTagQuerySql emits. The `d` alias is the mt_events table of the event query.
        var extracted = registration.ExtractValue(value);
        var schema = events.DatabaseSchemaName;
        var suffix = registration.TableSuffix;

        // Under conjoined tenancy seq_id is not unique across tenants (per-tenant sequences), so the
        // correlated subquery must also match tenant_id; the outer event query is already tenant-scoped.
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            return new WhereFragment(
                $"d.seq_id in (select seq_id from {schema}.mt_event_tag_{suffix} where value = ? and tenant_id = d.tenant_id)",
                extracted);
        }

        return new WhereFragment(
            $"d.seq_id in (select seq_id from {schema}.mt_event_tag_{suffix} where value = ?)",
            extracted);
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
