#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon.Internals;

/// <summary>
///     WHERE clause filter to limit event fetching to only the event types specified
/// </summary>
internal sealed class EventTypeFilter: ISqlFragment
{
    private readonly string[] _typeNames;

    public EventTypeFilter(EventGraph graph, IReadOnlyCollection<Type> eventTypes)
    {
        EventTypes = eventTypes;
        // We need to load events that are mapped to this event type, not just the event itself.
        var additionalAliases = graph.AliasesForEvents(eventTypes);
        _typeNames = eventTypes.Select(x => graph.EventMappingFor(x).Alias).Union(additionalAliases).ToArray();
    }

    public IReadOnlyCollection<Type> EventTypes { get; }

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append("d.type = ANY(");
        builder.AppendStringArrayParameter(_typeNames);
        builder.Append(')');
    }

}
