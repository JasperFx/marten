using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Internal;
using Marten.Schema.Identity.Sequences;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

// This little abomination is used strictly in write ahead code generation
internal class StandinDatabase: IMartenDatabase
{
    public StandinDatabase(StoreOptions options)
    {
        Providers = new ProviderGraph(options);
    }

    public IFeatureSchema[] BuildFeatureSchemas()
    {
        throw new NotImplementedException();
    }

    public string[] AllSchemaNames()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ISchemaObject> AllObjects()
    {
        throw new NotImplementedException();
    }

    public Task<SchemaMigration> CreateMigrationAsync(IFeatureSchema group, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public string ToDatabaseScript()
    {
        throw new NotImplementedException();
    }

    public Task WriteCreationScriptToFileAsync(string filename, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task WriteScriptsByTypeAsync(string directory, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<SchemaMigration> CreateMigrationAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<SchemaPatchDifference> ApplyAllConfiguredChangesToDatabaseAsync(
        AutoCreate? @override = null,
        ReconnectionOptions? reconnectionOptions = null,
        CancellationToken ct = default
    )
    {
        throw new NotImplementedException();
    }

    public Task AssertDatabaseMatchesConfigurationAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task AssertConnectivityAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public AutoCreate AutoCreate { get; }
    public Migrator Migrator { get; }
    public string Identifier { get; }

    public NpgsqlConnection CreateConnection()
    {
        throw new NotImplementedException();
    }

    public void DeleteAllDocuments()
    {
        throw new NotImplementedException();
    }

    public Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void DeleteDocumentsByType(Type documentType)
    {
        throw new NotImplementedException();
    }

    public Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void DeleteDocumentsExcept(params Type[] documentTypes)
    {
        throw new NotImplementedException();
    }

    public Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes)
    {
        throw new NotImplementedException();
    }

    public void CompletelyRemove(Type documentType)
    {
        throw new NotImplementedException();
    }

    public Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void CompletelyRemoveAll()
    {
        throw new NotImplementedException();
    }

    public Task CompletelyRemoveAllAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void DeleteAllEventData()
    {
        throw new NotImplementedException();
    }

    public Task DeleteAllEventDataAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void DeleteSingleEventStream(Guid streamId, string? tenantId = null)
    {
        throw new NotImplementedException();
    }

    public Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void DeleteSingleEventStream(string streamId, string? tenantId = null)
    {
        throw new NotImplementedException();
    }

    public Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void EnsureStorageExists(Type documentType)
    {
        throw new NotImplementedException();
    }

    public ValueTask EnsureStorageExistsAsync(Type featureType, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public Task ResetHiloSequenceFloor<T>(long floor)
    {
        throw new NotImplementedException();
    }

    public ISequences Sequences { get; }
    public ISequences BuildSequencesForMigration()
    {
        throw new NotImplementedException();
    }

    public IProviderGraph Providers { get; }
    public ShardStateTracker? Tracker { get; }

    public Task<IReadOnlyList<DbObjectName>> DocumentTables()
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<DbObjectName>> Functions()
    {
        throw new NotImplementedException();
    }

    public Task<Table> ExistingTableFor(Type type)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<DbObjectName>> SchemaTables(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<EventStoreStatistics> FetchEventStoreStatistics(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ShardState>> AllProjectionProgress(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<ShardState>> FetchProjectionProgressFor(ShardName[] names, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> ProjectionProgressFor(ShardName name, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public async Task<long?> FindEventStoreFloorAtTimeAsync(DateTimeOffset timestamp, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<long> FetchHighestEventSequenceNumber(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public NpgsqlConnection CreateConnection(ConnectionUsage connectionUsage = ConnectionUsage.ReadWrite)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        ((IDisposable)Tracker)?.Dispose();
    }
}
