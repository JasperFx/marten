#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Internal;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

/// <summary>
///     Governs the database structure and migration path for a single Marten database
/// </summary>
public interface IMartenDatabase: IDatabase, IConnectionSource<NpgsqlConnection>, IDocumentCleaner, IDisposable
{
    /// <summary>
    ///     Used to create new Hilo sequences
    /// </summary>
    ISequences Sequences { get; }


    IProviderGraph Providers { get; }

    /// <summary>
    ///     *If* a projection daemon has been started for this database, this
    ///     is the ShardStateTracker for the running daemon. This is useful in testing
    ///     scenarios
    /// </summary>
    ShardStateTracker Tracker { get; }

    /// <summary>
    ///     Ensures that the IDocumentStorage object for a document type is ready
    ///     and also attempts to update the database schema for any detected changes
    /// </summary>
    /// <param name="documentType"></param>
    void EnsureStorageExists(Type documentType);

    /// <summary>
    ///     Ensures that the IDocumentStorage object for a document type is ready
    ///     and also attempts to update the database schema for any detected changes
    /// </summary>
    /// <param name="featureType"></param>
    /// <param name="???"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    ValueTask EnsureStorageExistsAsync(Type featureType, CancellationToken token = default);

    /// <summary>
    ///     Set the minimum sequence number for a Hilo sequence for a specific document type
    ///     to the specified floor. Useful for migrating data between databases
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="floor"></param>
    Task ResetHiloSequenceFloor<T>(long floor);


    Task<IReadOnlyList<DbObjectName>> DocumentTables();
    Task<IReadOnlyList<DbObjectName>> Functions();
    Task<Table> ExistingTableFor(Type type);

    /// <summary>
    ///     Fetch a list of the existing tables in the database
    /// </summary>
    /// <param name="database"></param>
    /// <returns></returns>
    public Task<IReadOnlyList<DbObjectName>> SchemaTables(CancellationToken ct = default);

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
    Task<EventStoreStatistics> FetchEventStoreStatistics(
        CancellationToken token = default);

    /// <summary>
    ///     Check the current progress of all asynchronous projections
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <returns></returns>
    Task<IReadOnlyList<ShardState>> AllProjectionProgress(
        CancellationToken token = default);

    /// <summary>
    ///     Check the current progress of all asynchronous projections
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <returns></returns>
    Task<IReadOnlyList<ShardState>> FetchProjectionProgressFor(ShardName[] names,
        CancellationToken token = default);

    /// <summary>
    ///     Check the current progress of a single projection or projection shard
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<long> ProjectionProgressFor(ShardName name,
        CancellationToken token = default);

    NpgsqlConnection CreateConnection(ConnectionUsage connectionUsage = ConnectionUsage.ReadWrite);

    /// <summary>
    /// Find the position of the event store sequence just below the supplied timestamp. Will
    /// return null if there are no events below that time threshold
    /// </summary>
    /// <param name="timestamp"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<long?> FindEventStoreFloorAtTimeAsync(DateTimeOffset timestamp, CancellationToken token);

    /// <summary>
    /// Fetch the highest assigned event sequence number in this database
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<long> FetchHighestEventSequenceNumber(CancellationToken token = default);
}

public enum ConnectionUsage
{
    Read,
    ReadWrite
}
