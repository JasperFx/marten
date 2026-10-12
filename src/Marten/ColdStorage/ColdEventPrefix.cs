#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.ColdStorage;

/// <summary>
///     Makes an EventStatement read <c>mt_events_cold</c>, limited to the versions before the first hot one.
/// </summary>
internal sealed class ColdPrefixFilter: ISqlFragment
{
    private readonly long _beforeVersion;

    public ColdPrefixFilter(long beforeVersion)
    {
        _beforeVersion = beforeVersion;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("d.version < ");
        builder.AppendParameter(_beforeVersion);
    }
}

internal static class ColdEventPrefix
{
    /// <summary>
    ///     A stream whose events were moved keeps its last event hot, so moved events show up as a gap
    ///     before the first hot event. Streams that were never moved cost nothing extra.
    /// </summary>
    public static bool IsMissing(StoreOptions options, IReadOnlyList<IEvent> hot, long fromVersion, long version,
        DateTimeOffset? timestamp, out ISqlFragment? coldPrefix)
    {
        coldPrefix = null;
        if (options.ColdStorageProvider == null)
        {
            return false;
        }

        if (hot.Count == 0)
        {
            // Only a point-in-time read can land entirely before the hot tail.
            if (version == 0 && timestamp == null)
            {
                return false;
            }

            coldPrefix = new ColdPrefixFilter(long.MaxValue);
            return true;
        }

        if (hot[0].Version <= Math.Max(1, fromVersion))
        {
            return false;
        }

        coldPrefix = new ColdPrefixFilter(hot[0].Version);
        return true;
    }

    public static IReadOnlyList<IEvent> Prepend(IReadOnlyList<IEvent> cold, IReadOnlyList<IEvent> hot)
    {
        var all = new List<IEvent>(cold.Count + hot.Count);
        all.AddRange(cold);
        all.AddRange(hot);
        return all;
    }
}
