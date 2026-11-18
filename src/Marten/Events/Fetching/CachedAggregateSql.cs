#nullable enable
using System.Linq;
using JasperFx.Core;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

/// <summary>
///     #6086: SQL for a fetch that can't redo itself, a batched one, deciding in the database whether the cached
///     aggregate it took can be folded onto. The rules are the single fetch's (jasperfx#1050), which redoes itself
///     instead: no stream, a stream behind the cached aggregate (a restore, a rollback, a deleted stream), and, for
///     a plan that leaves archived events out, an archived stream or no event left past the cached version.
/// </summary>
internal static class CachedAggregateSql
{
    /// <summary>
    ///     <c>exists (...)</c>: the stream's row allows folding onto an aggregate cached at
    ///     <paramref name="cachedVersion" />.
    /// </summary>
    /// <param name="streamTenant">The tenant to read the stream row under, or null to read it by id alone.</param>
    /// <param name="eventTenant">The tenant to read the events under, or null to read them by stream alone.</param>
    /// <param name="liveEvents">
    ///     The plan reads only events that aren't archived (the Live plan). An archived stream, or one whose events
    ///     past the cached version were all archived, then can't be served from the cache. The Async plan reads
    ///     archived events too, and needs only some event past the cached version.
    /// </param>
    public static void WriteUsable<TId>(ICommandBuilder builder, EventGraph events, TId id, long cachedVersion,
        string? streamTenant, string? eventTenant, bool liveEvents) where TId : notnull
    {
        var schema = events.DatabaseSchemaName;

        builder.Append("exists (select 1 from ");
        builder.Append(schema);
        builder.Append(".mt_streams as s where s.id = ");
        builder.AppendParameter(id);
        if (streamTenant != null)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(streamTenant);
        }

        if (liveEvents)
        {
            builder.Append(" and s.is_archived = false");
        }

        builder.Append(" and s.version >= ");
        builder.AppendParameter(cachedVersion);
        builder.Append(" and (s.version = ");
        builder.AppendParameter(cachedVersion);
        builder.Append(" or exists (select 1 from ");
        builder.Append(schema);
        builder.Append(".mt_events as e where e.stream_id = ");
        builder.AppendParameter(id);
        if (eventTenant != null)
        {
            builder.Append(" and e.tenant_id = ");
            builder.AppendParameter(eventTenant);
        }

        builder.Append(" and e.version > ");
        builder.AppendParameter(cachedVersion);
        if (liveEvents)
        {
            builder.Append(" and e.is_archived = false");
        }

        builder.Append(")))");
    }

    /// <summary>
    ///     <c>exists (...)</c>: the stream is exactly at <paramref name="cachedVersion" />, which is what serving an
    ///     Inline aggregate from the cache takes.
    /// </summary>
    public static void WriteStreamAtVersion<TId>(ICommandBuilder builder, EventGraph events, TId id,
        long cachedVersion, string? streamTenant) where TId : notnull
    {
        builder.Append("exists (select 1 from ");
        builder.Append(events.DatabaseSchemaName);
        builder.Append(".mt_streams as s where s.id = ");
        builder.AppendParameter(id);
        if (streamTenant != null)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(streamTenant);
        }

        builder.Append(" and s.version = ");
        builder.AppendParameter(cachedVersion);
        builder.Append(')');
    }

    /// <summary>The event columns, as selected from <c>mt_events as d</c>.</summary>
    public static string EventFields(IEventStorage selector)
        => selector.SelectFields().Select(x => "d." + x).Join(", ");
}
