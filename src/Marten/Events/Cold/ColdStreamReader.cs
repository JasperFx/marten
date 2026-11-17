#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal.Sessions;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Cold;

/// <summary>
///     Which part of a stream a read wants, mirroring the version and timestamp filters of
///     <c>FetchStreamAsync</c>. The default is the whole stream.
/// </summary>
internal readonly record struct StreamWindow(long ThroughVersion, long FromVersion, DateTimeOffset? Timestamp)
{
    public bool Includes(long version, DateTimeOffset timestamp)
    {
        if (ThroughVersion > 0 && version > ThroughVersion) return false;
        if (FromVersion > 0 && version < FromVersion) return false;
        return Timestamp == null || timestamp <= Timestamp.Value;
    }
}

/// <summary>
///     Reads a stream's cold boundary alongside its events, and puts the cold events in front of the warm ones.
/// </summary>
internal static class ColdStreamReader
{
    /// <summary>
    ///     Appends <c>select cold_version from mt_streams ...</c> as its own statement. Pair with
    ///     <see cref="ReadBoundaryAsync" />.
    /// </summary>
    public static void WriteBoundaryQuery(EventGraph events, ICommandBuilder builder, string streamKey, string tenantId)
    {
        builder.Append($"select {StreamsTable.ColdVersionColumn} from {events.DatabaseSchemaName}.{StreamsTable.TableName} where id = ");
        builder.AppendParameter(streamKey);

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(tenantId);
        }
    }

    /// <summary>
    ///     The stream's cold version, or 0 when it has no cold part. Leaves the reader on the boundary's result.
    /// </summary>
    public static async Task<long> ReadBoundaryAsync(DbDataReader reader, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false) &&
            !await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    ///     The tenant whose stream is read: the default tenant for a global aggregate, otherwise the session's.
    /// </summary>
    public static string TenantFor(QuerySession session, bool isGlobal) =>
        isGlobal ? StorageConstants.DefaultTenantId : session.TenantId;

    /// <summary>
    ///     The stream's cold events through <paramref name="coldVersion" /> that fall in <paramref name="window" />,
    ///     followed by the warm events above it. Must not run while a reader is open on the session.
    /// </summary>
    public static async Task<IReadOnlyList<IEvent>> PrependColdAsync(QuerySession session, string streamKey,
        string tenantId, long coldVersion, IReadOnlyList<IEvent> warm, StreamWindow window, CancellationToken token)
    {
        if (coldVersion <= 0) return warm;

        var events = session.Options.EventGraph;
        var store = events.ColdEventStore ?? throw new InvalidOperationException(
            $"Stream '{streamKey}' has a cold part through version {coldVersion}, but no cold event store is configured. Use StoreOptions.Events.UseColdEventStore().");

        // Only as far as the read wants, and not at all when it starts above the cold part
        var through = window.ThroughVersion > 0 ? Math.Min(window.ThroughVersion, coldVersion) : coldVersion;

        var all = new List<IEvent>(warm.Count + (int)Math.Min(through, 1024));
        if (window.FromVersion <= through)
        {
            var order = new ColdReadOrder(store, through);
            await foreach (var cold in store.ReadAsync(session, streamKey, through, token).WithCancellation(token).ConfigureAwait(false))
            {
                order.Check(streamKey, cold.Version);
                if (!window.Includes(cold.Version, cold.Timestamp)) continue;

                all.Add(await deserializeAsync(events, cold, streamKey, tenantId, token).ConfigureAwait(false));
            }
        }

        appendWarm(all, warm, coldVersion);
        return all;
    }

    /// <summary>
    ///     Appends <c>select id, cold_version from mt_streams ...</c> for those of <paramref name="streamKeys" />
    ///     that have a cold part, as its own statement. Pair with <see cref="ReadBoundariesAsync" />.
    /// </summary>
    public static void WriteBoundariesQuery(EventGraph events, ICommandBuilder builder, string[] streamKeys, string tenantId)
    {
        builder.Append($"select id, {StreamsTable.ColdVersionColumn} from {events.DatabaseSchemaName}.{StreamsTable.TableName} where id = ANY(");
        builder.AppendParameter(streamKeys);
        builder.Append($") and {StreamsTable.ColdVersionColumn} is not null");

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(tenantId);
        }
    }

    /// <summary>
    ///     The cold version of every stream that has one. Leaves the reader on the boundaries' result.
    /// </summary>
    public static async Task<Dictionary<string, long>> ReadBoundariesAsync(DbDataReader reader, CancellationToken token)
    {
        var boundaries = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var key = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            boundaries[key] = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
        }

        return boundaries;
    }

    /// <summary>
    ///     Each stream's whole history: the cold events of the streams in <paramref name="coldVersions" />, all
    ///     read with one <see cref="IColdEventStore.ReadManyAsync" />, followed by the warm events above each
    ///     boundary. <paramref name="warm" /> holds each stream's warm events in version order. Must not run while
    ///     a reader is open on the session.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<IEvent>>> PrependColdAsync(
        QuerySession session, string tenantId, IReadOnlyDictionary<string, long> coldVersions,
        IReadOnlyDictionary<string, List<IEvent>> warm, CancellationToken token)
    {
        var streams = new Dictionary<string, IReadOnlyList<IEvent>>(StringComparer.Ordinal);
        foreach (var (key, events) in warm)
        {
            if (!coldVersions.ContainsKey(key)) streams[key] = events;
        }

        if (coldVersions.Count == 0) return streams;

        var graph = session.Options.EventGraph;
        var store = graph.ColdEventStore ?? throw new InvalidOperationException(
            $"{coldVersions.Count} stream(s) have a cold part, but no cold event store is configured. Use StoreOptions.Events.UseColdEventStore().");

        var requests = coldVersions.Select(x => new ColdStreamRead(x.Key, x.Value)).ToArray();
        var orders = requests.ToDictionary(x => x.StreamKey, x => new ColdReadOrder(store, x.ThroughVersion), StringComparer.Ordinal);
        var all = requests.ToDictionary(x => x.StreamKey, _ => new List<IEvent>(), StringComparer.Ordinal);

        await foreach (var (streamKey, cold) in store.ReadManyAsync(session, requests, token).WithCancellation(token).ConfigureAwait(false))
        {
            if (streamKey == null || !orders.TryGetValue(streamKey, out var order))
            {
                throw new InvalidOperationException(
                    $"{store.GetType().Name} returned an event of stream '{streamKey}', which it was not asked for.");
            }

            order.Check(streamKey, cold.Version);
            all[streamKey].Add(await deserializeAsync(graph, cold, streamKey, tenantId, token).ConfigureAwait(false));
        }

        foreach (var (key, events) in all)
        {
            if (warm.TryGetValue(key, out var warmEvents)) appendWarm(events, warmEvents, coldVersions[key]);
            streams[key] = events;
        }

        return streams;
    }

    private static async Task<IEvent> deserializeAsync(EventGraph events, ColdEvent cold, string streamKey,
        string tenantId, CancellationToken token)
    {
        var @event = await events.DeserializeAsync(cold, token).ConfigureAwait(false);
        @event.StreamKey = streamKey;
        @event.TenantId = tenantId;
        return @event;
    }

    // A warm event at or below the boundary would be read twice; the boundary wins.
    private static void appendWarm(List<IEvent> all, IReadOnlyList<IEvent> warm, long coldVersion)
    {
        foreach (var @event in warm)
        {
            if (@event.Version > coldVersion) all.Add(@event);
        }
    }

    /// <summary>
    ///     Holds a cold event store to its contract for one stream: oldest first, at most one event per version,
    ///     none above the version it was asked for.
    /// </summary>
    private sealed class ColdReadOrder(IColdEventStore store, long through)
    {
        private long _previous;

        public void Check(string streamKey, long version)
        {
            if (version <= _previous || version > through)
            {
                throw new InvalidOperationException(
                    $"{store.GetType().Name} returned version {version} of stream '{streamKey}' after version {_previous}. A cold event store must return at most one event per version, oldest first, none above the version it was asked for ({through}).");
            }

            _previous = version;
        }
    }
}
