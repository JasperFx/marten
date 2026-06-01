#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events.Tags;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

/// <summary>
/// Shared SQL-emission helpers for DCB queries when <see cref="DcbStorageMode.HStore"/>
/// is in effect. The same containment predicate shape is needed by
/// <see cref="EventsExistByTagsHandler"/>, <see cref="FetchForWritingByTagsHandler"/>,
/// and the inline query builder on <see cref="EventStore"/>; this helper keeps the
/// SQL identical across all three.
/// </summary>
internal static class HStoreDcbQueryFragment
{
    /// <summary>
    /// Append the WHERE-clause OR group that matches any of the supplied tag conditions
    /// against the <c>mt_events.tags</c> hstore column on the alias <paramref name="eventAlias"/>.
    /// Each condition becomes <c>(e.tags @&gt; hstore(@key, @value)[ and e.type = @type])</c>.
    /// </summary>
    public static void AppendOrPredicate(ICommandBuilder builder, EventGraph events,
        IReadOnlyList<EventTagQueryCondition> conditions, string eventAlias)
    {
        builder.Append('(');
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0) builder.Append(" or ");

            var condition = conditions[i];
            var registration = events.FindTagType(condition.TagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{condition.TagType.Name}' is not registered. Call RegisterTagType<{condition.TagType.Name}>() first.");

            var rawValue = registration.ExtractValue(condition.TagValue);
            var stringValue = rawValue?.ToString()
                              ?? throw new InvalidOperationException(
                                  $"Tag value for '{condition.TagType.Name}' is null after extraction.");

            builder.Append('(');
            builder.Append(eventAlias);
            builder.Append(".tags @> hstore(");
            builder.AppendParameter(registration.TableSuffix);
            builder.Append(", ");
            builder.AppendParameter(stringValue);
            builder.Append(')');

            if (condition.EventType != null)
            {
                builder.Append(" and ");
                builder.Append(eventAlias);
                builder.Append(".type = ");
                var eventTypeName = events.EventMappingFor(condition.EventType).EventTypeName;
                builder.AppendParameter(eventTypeName);
            }

            builder.Append(')');
        }

        builder.Append(')');
    }

    /// <summary>
    /// String-builder variant of <see cref="AppendOrPredicate(ICommandBuilder, EventGraph, IReadOnlyList{EventTagQueryCondition}, string)"/>,
    /// used by the <see cref="EventStore.BuildTagQuerySql"/> path that constructs SQL into a
    /// <see cref="System.Text.StringBuilder"/> with positional <c>@p{n}</c> parameters.
    /// </summary>
    public static void AppendOrPredicate(System.Text.StringBuilder sb, EventGraph events,
        IReadOnlyList<EventTagQueryCondition> conditions, string eventAlias, List<object> paramValues)
    {
        sb.Append('(');
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0) sb.Append(" or ");

            var condition = conditions[i];
            var registration = events.FindTagType(condition.TagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{condition.TagType.Name}' is not registered. Call RegisterTagType<{condition.TagType.Name}>() first.");

            var rawValue = registration.ExtractValue(condition.TagValue);
            var stringValue = rawValue?.ToString()
                              ?? throw new InvalidOperationException(
                                  $"Tag value for '{condition.TagType.Name}' is null after extraction.");

            sb.Append('(');
            sb.Append(eventAlias);
            sb.Append(".tags @> hstore(@p");
            sb.Append(paramValues.Count);
            paramValues.Add(registration.TableSuffix);
            sb.Append(", @p");
            sb.Append(paramValues.Count);
            paramValues.Add(stringValue);
            sb.Append(')');

            if (condition.EventType != null)
            {
                sb.Append(" and ");
                sb.Append(eventAlias);
                sb.Append(".type = @p");
                sb.Append(paramValues.Count);
                var eventTypeName = events.EventMappingFor(condition.EventType).EventTypeName;
                paramValues.Add(eventTypeName);
            }

            sb.Append(')');
        }

        sb.Append(')');
    }
}
