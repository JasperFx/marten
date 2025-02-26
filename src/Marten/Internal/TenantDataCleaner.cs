using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Schema;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Internal;

internal class TenantDataCleaner
{
    private readonly string _tenantId;
    private readonly DocumentStore _store;

    public TenantDataCleaner(string tenantId, DocumentStore store)
    {
        _tenantId = tenantId;
        _store = store;
    }

    public async Task ExecuteAsync(CancellationToken token)
    {
        var logger = _store.Options.LogFactory?.CreateLogger<DocumentStore>() ?? NullLogger<DocumentStore>.Instance;

        var database = await _store.Storage.FindOrCreateDatabase(_tenantId).ConfigureAwait(false);
        if (_store.Options is { TenantPartitions: not null})
        {
            await _store.Options.TenantPartitions.Partitions.DropPartitionFromAllTablesForValue(
                (PostgresqlDatabase)database, logger, _tenantId, token).ConfigureAwait(false);
        }

        var tables = await database.SchemaTables(token).ConfigureAwait(false);

        var builder = new BatchBuilder();

        // Clear events out of a single database
        if (_store.Options.Events.TenancyStyle == TenancyStyle.Single && _store.Options.Tenancy is not DefaultTenancy)
        {
            // if all tenant data is in a specific database, just wipe it here
            logger.LogInformation("Deleting all event data in the database '{DbIdentifier}' for tenant '{TenantId}'", database.Identifier, _tenantId);
            await database.DeleteAllEventDataAsync(token).ConfigureAwait(false);
        }

        bool foundTables = false;
        Action<DbObjectName> deleteTenantedDataIfExists = tableName =>
        {
            if (tables.Contains(tableName))
            {
                foundTables = true;
                logger.LogInformation("Trying to delete tenant information from table {Table} for tenant {TenantId}", tableName.QualifiedName, _tenantId);
                builder.StartNewCommand();
                builder.Append($"delete from {tableName.QualifiedName} where tenant_id = ");
                builder.AppendParameter(_tenantId);
            }
        };

        Action<DbObjectName> deleteDataIfExists = tableName =>
        {
            if (tables.Contains(tableName))
            {
                foundTables = true;
                logger.LogInformation("Trying to delete tenant information from table {Table} for tenant {TenantId}", tableName.QualifiedName, _tenantId);
                builder.StartNewCommand();
                builder.Append($"delete from {tableName.QualifiedName}");
            }
        };

        if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var eventsSchema = _store.Options.Events.DatabaseSchemaName;
            deleteTenantedDataIfExists(new DbObjectName(eventsSchema, "mt_events"));
            deleteTenantedDataIfExists(new DbObjectName(eventsSchema, "mt_streams"));
        }

        if (_store.Options.Tenancy is DefaultTenancy)
        {
            deleteDataForSingleDatabase(deleteTenantedDataIfExists);
        }
        else
        {
            deleteDataForTenantDatabase(deleteTenantedDataIfExists, deleteDataIfExists);
        }

        if (!foundTables) return;

        var batch = builder.Compile();
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        batch.Connection = conn;
        await batch.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await conn.CloseAsync().ConfigureAwait(false);
    }

    private void deleteDataForTenantDatabase(Action<DbObjectName> deleteTenantedDataIfExists, Action<DbObjectName> deleteDataIfExists)
    {
        var allTypes = _store.Options.Storage.AllDocumentMappings.OfType<IDocumentType>()
            .Select(x => x.DocumentType)
            .ToList();

        var types = allTypes
            .TopologicalSort(type => _store.Options.Storage.GetTypeDependencies(type))

            // Need to delete data from the downstream tables first!
            .Reverse()
            .ToArray();

        foreach (var type in types)
        {
            var mapping = _store.Options.Storage.MappingFor(type);
            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                deleteTenantedDataIfExists(mapping.TableName);
            }
            else
            {
                deleteDataIfExists(mapping.TableName);
            }
        }
    }

    private void deleteDataForSingleDatabase(Action<DbObjectName> deleteTenantedDataIfExists)
    {
        var tenantedTypes = _store.Options.Storage.AllDocumentMappings.OfType<IDocumentType>()
            .Where(x => x.TenancyStyle == TenancyStyle.Conjoined)
            .Select(x => x.DocumentType)
            .ToList();

        var types = tenantedTypes
            .TopologicalSort(type => _store.Options.Storage.GetTypeDependencies(type))

            // Need to delete data from the downstream tables first!
            .Reverse()
            .ToArray();

        foreach (var type in types)
        {
            var mapping = _store.Options.Storage.MappingFor(type);
            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                deleteTenantedDataIfExists(mapping.TableName);
            }
        }
    }
}
