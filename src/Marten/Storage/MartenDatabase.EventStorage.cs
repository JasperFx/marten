#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Linq.QueryHandlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage;

public partial class MartenDatabase
{
    public async Task<long?> FindEventStoreFloorAtTimeAsync(DateTimeOffset timestamp, CancellationToken token)
    {
        var sql =
            $"select seq_id from {Options.Events.DatabaseSchemaName}.mt_events where timestamp >= :timestamp order by seq_id limit 1";
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();

        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            var raw = await conn.CreateCommand(sql).With("timestamp", timestamp.ToUniversalTime(), NpgsqlDbType.TimestampTz).ExecuteScalarAsync(token).ConfigureAwait(false);
            return raw is DBNull ? null : (long?)raw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetch the highest assigned event sequence number in this database
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> FetchHighestEventSequenceNumber(CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        var highest = (long)await conn
            .CreateCommand($"select last_value from {Options.Events.DatabaseSchemaName}.mt_events_sequence;")
            .ExecuteScalarAsync(token).ConfigureAwait(false);

        return highest;
    }


    /// <summary>
    ///     Fetch the current size of the event store tables, including the current value
    ///     of the event sequence number
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<EventStoreStatistics> FetchEventStoreStatistics(
        CancellationToken token = default)
    {
        var sql = $@"
select count(*) from {Options.Events.DatabaseSchemaName}.mt_events;
select count(*) from {Options.Events.DatabaseSchemaName}.mt_streams;
select last_value from {Options.Events.DatabaseSchemaName}.mt_events_sequence;
";

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statistics = new EventStoreStatistics();

        await using var conn = CreateConnection();

        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var reader = await conn.CreateCommand(sql).ExecuteReaderAsync(token).ConfigureAwait(false);

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            statistics.EventCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        await reader.NextResultAsync(token).ConfigureAwait(false);

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            statistics.StreamCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        await reader.NextResultAsync(token).ConfigureAwait(false);

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            statistics.EventSequenceNumber = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        }

        return statistics;
    }


    /// <summary>
    ///     Check the current progress of all asynchronous projections
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(
        CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(
            new ProjectionProgressStatement(Options.EventGraph),
            new ShardStateSelector(Options.EventGraph));

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        var builder = new CommandBuilder();
        handler.ConfigureCommand(builder, null);

        await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
        return await handler.HandleAsync(reader, null, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ShardState>> FetchProjectionProgressFor(ShardName[] names, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(
            new ProjectionProgressStatement(Options.EventGraph){Names = names},
            new ShardStateSelector(Options.EventGraph));

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        var builder = new CommandBuilder();
        handler.ConfigureCommand(builder, null);

        await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
        return await handler.HandleAsync(reader, null, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Check the current progress of a single projection or projection shard
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> ProjectionProgressFor(ShardName name,
        CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new ProjectionProgressStatement(Options.EventGraph) { Name = name };

        var handler = new OneResultHandler<ShardState>(statement,
            new ShardStateSelector(Options.EventGraph), true, false);

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        var builder = new CommandBuilder();
        handler.ConfigureCommand(builder, null);

        await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
        var state = await handler.HandleAsync(reader, null, token).ConfigureAwait(false);

        return state?.Sequence ?? 0;
    }

    /// <summary>
    ///     *If* a projection daemon has been started for this database, this
    ///     is the ShardStateTracker for the running daemon. This is useful in testing
    ///     scenarios
    /// </summary>
    public ShardStateTracker Tracker { get; private set; }

    internal IProjectionDaemon StartProjectionDaemon(DocumentStore store, ILogger? logger = null)
    {
        logger ??= store.Options.LogFactory?.CreateLogger<ProjectionDaemon>() ??
                   store.Options.DotNetLogger ?? NullLogger.Instance;

        var detector = new HighWaterDetector(this, Options.EventGraph, logger);

        return new ProjectionDaemon(store, this, logger, detector, new AgentFactory(store));
    }
}
