#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
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

        var source = store.ReadAsync(session, streamKey, coldVersion, token);

        var all = new List<IEvent>(warm.Count + (int)Math.Min(coldVersion, 1024));
        long previous = 0;
        await foreach (var cold in source.WithCancellation(token).ConfigureAwait(false))
        {
            if (cold.Version <= previous || cold.Version > coldVersion)
            {
                throw new InvalidOperationException(
                    $"{store.GetType().Name} returned version {cold.Version} of stream '{streamKey}' after version {previous}. A cold event store must return at most one event per version, oldest first, none above the cold version {coldVersion}.");
            }

            previous = cold.Version;
            if (!window.Includes(cold.Version, cold.Timestamp)) continue;

            var @event = await events.DeserializeAsync(cold, token).ConfigureAwait(false);
            @event.StreamKey = streamKey;
            @event.TenantId = tenantId;
            all.Add(@event);
        }

        // A warm event at or below the boundary would be folded twice; the boundary wins.
        foreach (var @event in warm)
        {
            if (@event.Version > coldVersion) all.Add(@event);
        }

        return all;
    }
}
