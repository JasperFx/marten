#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Schema;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Storage;

public partial class MartenDatabase: IDocumentCleaner
{
    public static string DropAllFunctionSql = @"
SELECT format('DROP FUNCTION IF EXISTS %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE  p.proname like 'mt_%' and n.nspname = ANY(:schemas)";

    public static readonly string DropFunctionSql = @"
SELECT format('DROP FUNCTION IF EXISTS %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE  p.proname = '{0}'
AND    n.nspname = '{1}';";

    public static readonly string DropAllSequencesSql = @"SELECT format('DROP SEQUENCE IF EXISTS %s.%s;'
             ,s.sequence_schema
             ,s.sequence_name)
FROM   information_schema.sequences s
WHERE  s.sequence_name like 'mt_%' and s.sequence_schema = ANY(:schemas);";

    public void DeleteAllDocuments()
    {
        DeleteAllDocumentsAsync().GetAwaiter().GetResult();
    }

    public async Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var schemas = AllSchemaNames();
        var tables = await conn.ExistingTablesAsync("mt_%", schemas, ct: ct).ConfigureAwait(false);

        if (!tables.Any())
        {
            return;
        }

        var builder = new CommandBuilder();
        foreach (var table in tables) builder.Append($"truncate {table} cascade;");

        await builder.ExecuteNonQueryAsync(conn, ct).ConfigureAwait(false);
    }

    public void DeleteDocumentsByType(Type documentType)
    {
        EnsureStorageExists(documentType);
        var storage = Providers.StorageFor(documentType);
        storage.TruncateDocumentStorage(this);
    }

    public async Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default)
    {
        await EnsureStorageExistsAsync(documentType, ct).ConfigureAwait(false);
        var storage = Providers.StorageFor(documentType);
        await storage.TruncateDocumentStorageAsync(this, ct).ConfigureAwait(false);
    }

    public void DeleteDocumentsExcept(params Type[] documentTypes)
    {
        var documentMappings = _options.Storage.DocumentMappingsWithSchema.Where(x => !documentTypes.Contains(x.DocumentType));
        foreach (var mapping in documentMappings)
        {
            var storage = Providers.StorageFor(mapping.DocumentType);
            storage.TruncateDocumentStorage(this);
        }
    }

    public async Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes)
    {
        var documentMappings = _options.Storage.DocumentMappingsWithSchema.Where(x => !documentTypes.Contains(x.DocumentType));
        foreach (var mapping in documentMappings)
        {
            var storage = Providers.StorageFor(mapping.DocumentType);
            await storage.TruncateDocumentStorageAsync(this, ct).ConfigureAwait(false);
        }
    }

    public void CompletelyRemove(Type documentType)
    {
        var mapping = _options.Storage.MappingFor(documentType);
        using var conn = CreateConnection();
        conn.Open();

        var writer = new StringWriter();
        foreach (var schemaObject in ((IFeatureSchema)mapping.Schema).Objects)
            schemaObject.WriteDropStatement(_options.Advanced.Migrator, writer);

        var sql = writer.ToString();
        var cmd = conn.CreateCommand(sql);

        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            if (cmd != null)
            {
                e.Data[nameof(NpgsqlCommand)] = cmd;
            }
            MartenExceptionTransformer.WrapAndThrow(e);
        }
    }

    public async Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default)
    {
        var mapping = _options.Storage.MappingFor(documentType);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var writer = new StringWriter();
        foreach (var schemaObject in ((IFeatureSchema)mapping.Schema).Objects)
            schemaObject.WriteDropStatement(_options.Advanced.Migrator, writer);

        var sql = writer.ToString();
        var cmd = conn.CreateCommand(sql);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(cmd, e);
        }
    }


    public void CompletelyRemoveAll()
    {
        CompletelyRemoveAllAsync().GetAwaiter().GetResult();
    }

    public async Task CompletelyRemoveAllAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var schemas = AllSchemaNames();
        var tables = await conn.ExistingTablesAsync("mt_%", schemas, ct: ct).ConfigureAwait(false);

        var builder = new CommandBuilder();


        foreach (var table in tables) builder.Append($"DROP TABLE IF EXISTS {table} CASCADE;");

        var functionDrops = await conn.CreateCommand(DropAllFunctionSql)
            .With("schemas", schemas)
            .FetchListAsync<string>(cancellation: ct).ConfigureAwait(false);
        foreach (var functionDrop in functionDrops) builder.Append(functionDrop);

        var sequenceDrops = await conn.CreateCommand(DropAllSequencesSql)
            .With("schemas", schemas)
            .FetchListAsync<string>(cancellation: ct).ConfigureAwait(false);
        foreach (var sequenceDrop in sequenceDrops) builder.Append(sequenceDrop);

        if (tables.Any() || functionDrops.Any() || sequenceDrops.Any())
        {
            var cmd = builder.Compile();
            cmd.Connection = conn;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        ResetSchemaExistenceChecks();
    }

    public void DeleteAllEventData()
    {
        using var connection = CreateConnection();
        connection.Open();
        var deleteEventDataSql = toDeleteEventDataSql();
        connection.CreateCommand(deleteEventDataSql).ExecuteNonQuery();
    }

    public async Task DeleteAllEventDataAsync(CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var deleteEventDataSql = toDeleteEventDataSql();
        await connection.CreateCommand(deleteEventDataSql, tx).ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }


    public void DeleteSingleEventStream(Guid streamId, string? tenantId = null)
    {
        DeleteSingleEventStream<Guid>(streamId, tenantId);
    }

    public void DeleteSingleEventStream(string streamId, string? tenantId = null)
    {
        DeleteSingleEventStream<string>(streamId, tenantId);
    }

    public Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null, CancellationToken ct = default)
    {
        return DeleteSingleEventStreamAsync<Guid>(streamId, tenantId, ct);
    }

    public Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null, CancellationToken ct = default)
    {
        return DeleteSingleEventStreamAsync<string>(streamId, tenantId, ct);
    }

    private string toDeleteEventDataSql()
    {
        return $@"
DO $$ BEGIN
ALTER SEQUENCE IF EXISTS {_options.Events.DatabaseSchemaName}.mt_events_sequence RESTART WITH 1;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_events' AND table_schema = '{_options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {_options.Events.DatabaseSchemaName}.mt_events CASCADE; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_streams' AND table_schema = '{_options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {_options.Events.DatabaseSchemaName}.mt_streams CASCADE; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_mark_event_progression' AND table_schema = '{_options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {_options.Events.DatabaseSchemaName}.mt_mark_event_progression CASCADE; END IF;
END; $$;
";
    }

    private void DeleteSingleEventStream<T>(T streamId, string? tenantId = null)
    {
        if (typeof(T) != _options.EventGraph.GetStreamIdType())
        {
            throw new ArgumentException(
                $"{nameof(streamId)} should  be of type {_options.EventGraph.GetStreamIdType()}", nameof(streamId));
        }

        using var conn = CreateConnection();
        var streamsWhere = "id = :id";
        var eventsWhere = "stream_id = :id";

        if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var tenantPart = " AND tenant_id = :tenantId";
            streamsWhere += tenantPart;
            eventsWhere += tenantPart;
        }

        var cmd = conn.CreateCommand(
            $"delete from {_options.Events.DatabaseSchemaName}.mt_events where {eventsWhere};delete from {_options.Events.DatabaseSchemaName}.mt_streams where {streamsWhere}");
        cmd.AddNamedParameter("id", streamId);

        if (_options.Events.TenancyStyle == TenancyStyle.Conjoined && tenantId.IsNotEmpty())
        {
            cmd.AddNamedParameter("tenantId", tenantId);
        }

        conn.Open();

        cmd.ExecuteNonQuery();
    }

    private async Task DeleteSingleEventStreamAsync<T>(T streamId, string? tenantId = null,
        CancellationToken ct = default)
    {
        if (typeof(T) != _options.EventGraph.GetStreamIdType())
        {
            throw new ArgumentException(
                $"{nameof(streamId)} should  be of type {_options.EventGraph.GetStreamIdType()}", nameof(streamId));
        }

        await using var conn = CreateConnection();
        var streamsWhere = "id = :id";
        var eventsWhere = "stream_id = :id";

        if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var tenantPart = " AND tenant_id = :tenantId";
            streamsWhere += tenantPart;
            eventsWhere += tenantPart;
        }

        var cmd = conn.CreateCommand(
            $"delete from {_options.Events.DatabaseSchemaName}.mt_events where {eventsWhere};delete from {_options.Events.DatabaseSchemaName}.mt_streams where {streamsWhere}");
        cmd.AddNamedParameter("id", streamId);

        if (_options.Events.TenancyStyle == TenancyStyle.Conjoined && tenantId.IsNotEmpty())
        {
            cmd.AddNamedParameter("tenantId", tenantId);
        }

        await conn.OpenAsync(ct).ConfigureAwait(false);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
