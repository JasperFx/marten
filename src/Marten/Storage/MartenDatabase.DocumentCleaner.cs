#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Operations;
using Marten.Events.Schema;
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

    public async Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var schemas = AllSchemaNames();

        // jasperfx#809 (#5740): projection start floors are configuration an operator or a first start
        // pinned, not data, and they have to outlive a data reset as they outlive rebuild teardown. A reset
        // that cleared them would make the next first start re-pin at the current high water and silently
        // skip everything appended in between. CompletelyRemoveAllAsync still drops the table.
        var tables = (await conn.ExistingTablesAsync("mt_%", schemas, ct: ct).ConfigureAwait(false))
            .Where(x => !x.Name.EqualsIgnoreCase(ProjectionFloorTable.Name))
            // jasperfx#1215 / #1222: the same goes for the ceilings and cursors of rewrites and backfills
            .Where(x => !x.Name.EqualsIgnoreCase(EventRangeMarkTable.Name))
            .ToList();

        if (!tables.Any())
        {
            return;
        }

        var builder = new CommandBuilder();
        foreach (var table in tables) builder.Append($"truncate {table} cascade;");

        await conn.ExecuteNonQueryAsync(builder, ct).ConfigureAwait(false);
    }

    public async Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default)
    {
        await EnsureStorageExistsAsync(documentType, ct).ConfigureAwait(false);
        var storage = Providers.StorageFor(documentType);
        await storage.TruncateDocumentStorageAsync(this, ct).ConfigureAwait(false);
    }

    public async Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes)
    {
        var documentMappings =
            Options.Storage.DocumentMappingsWithSchema.Where(x => !documentTypes.Contains(x.DocumentType));
        foreach (var mapping in documentMappings)
        {
            var storage = Providers.StorageFor(mapping.DocumentType);
            await storage.TruncateDocumentStorageAsync(this, ct).ConfigureAwait(false);
        }
    }

    public async Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default)
    {
        var mapping = Options.Storage.MappingFor(documentType);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var writer = new StringWriter();
        foreach (var schemaObject in ((IFeatureSchema)mapping.Schema).Objects)
            schemaObject.WriteDropStatement(Options.Advanced.Migrator, writer);

        var sql = writer.ToString();
        var cmd = conn.CreateCommand(sql);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(Options.Advanced.ExceptionTransforms, cmd, e);
        }
    }

    public async Task CompletelyRemoveAllAsync(CancellationToken ct = default)
    {
        // #5688: see DeleteAllEventDataAsync
        await Options.EventGraph.WaitForPendingTombstonesAsync().ConfigureAwait(false);

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var schemas = AllSchemaNames();
        var tables = await conn.ExistingTablesAsync("mt_%", schemas, ct: ct).ConfigureAwait(false);

        var builder = new CommandBuilder();

        // Drop extended schema objects (e.g., EF Core entity tables) registered via StoreOptions
        foreach (var schemaObject in Options.Storage.ExtendedSchemaObjects)
        {
            builder.Append($"DROP TABLE IF EXISTS {schemaObject.Identifier} CASCADE;");
        }

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

    public async Task DeleteAllEventDataAsync(CancellationToken ct = default)
    {
        // #5688: a failed save's tombstones are written in the background, at the sequence numbers it
        // drew. This wipe restarts mt_events_sequence at 1, so one landing after it would occupy a
        // number the sequence is about to hand out again, and the next append to reach it fails on
        // pkey_mt_events_seq_id. Let the writes already started finish first.
        await Options.EventGraph.WaitForPendingTombstonesAsync().ConfigureAwait(false);

        await EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var deleteEventDataSql = toDeleteEventDataSql();
        await connection.CreateCommand(deleteEventDataSql, tx).ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
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
ALTER SEQUENCE IF EXISTS {Options.Events.DatabaseSchemaName}.mt_events_sequence RESTART WITH 1;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_events' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {Options.Events.DatabaseSchemaName}.mt_events CASCADE; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_streams' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {Options.Events.DatabaseSchemaName}.mt_streams CASCADE; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = '{StreamIdentityEnforcementTable.TableName}' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN TRUNCATE TABLE {Options.Events.DatabaseSchemaName}.{StreamIdentityEnforcementTable.TableName}; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = 'mt_event_progression' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN delete from {Options.Events.DatabaseSchemaName}.mt_event_progression; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = '{EventProgressionSkippingTable.Name}' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN delete from {Options.Events.DatabaseSchemaName}.{EventProgressionSkippingTable.Name}; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = '{LateEventsTable.Name}' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN delete from {Options.Events.DatabaseSchemaName}.{LateEventsTable.Name}; END IF;
IF EXISTS(SELECT * FROM information_schema.tables
WHERE table_name = '{ParkedIdentitiesTable.Name}' AND table_schema = '{Options.Events.DatabaseSchemaName}')
THEN delete from {Options.Events.DatabaseSchemaName}.{ParkedIdentitiesTable.Name}; END IF;
END; $$;
DO $$ DECLARE r record; BEGIN
FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = '{Options.Events.DatabaseSchemaName}' AND tablename LIKE 'mt\_slice\_index\_%' LOOP
EXECUTE format('delete from %I.%I', '{Options.Events.DatabaseSchemaName}', r.tablename);
END LOOP;
END; $$;
";
    }

    /// <summary>
    ///     The same delete as <c>IEventStoreOperations.TombstoneStream</c>, run outside a session: the stream's tag
    ///     rows, events, stream row and identity row, and (#6078) its DCB tag versions, which move, and are deleted
    ///     where no other stream carries the tag. See <see cref="DeleteStreamOperation" />.
    /// </summary>
    /// <remarks>
    ///     Under conjoined tenancy with no tenant, the stream is deleted in every tenant that has one with this id,
    ///     one tenant at a time in one transaction, as the delete always reached all of them.
    /// </remarks>
    private async Task DeleteSingleEventStreamAsync<T>(T streamId, string? tenantId = null,
        CancellationToken ct = default) where T : notnull
    {
        if (typeof(T) != Options.EventGraph.GetStreamIdType())
        {
            throw new ArgumentException(
                $"{nameof(streamId)} should  be of type {Options.EventGraph.GetStreamIdType()}", nameof(streamId));
        }

        await EnsureStorageExistsAsync(typeof(IEvent), ct).ConfigureAwait(false);

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var tenants = new List<string>();
        if (Options.Events.TenancyStyle != TenancyStyle.Conjoined)
        {
            tenants.Add(tenantId ?? JasperFx.StorageConstants.DefaultTenantId);
        }
        else if (tenantId.IsNotEmpty())
        {
            tenants.Add(tenantId);
        }
        else
        {
            var find = conn.CreateCommand(
                $"select distinct tenant_id from {Options.Events.DatabaseSchemaName}.{StreamsTable.TableName} where id = :id order by tenant_id", tx);
            find.AddNamedParameter("id", streamId);
            await using var reader = await find.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tenants.Add(await reader.GetFieldValueAsync<string>(0, ct).ConfigureAwait(false));
            }
        }

        foreach (var tenant in tenants)
        {
            var builder = new BatchBuilder { TenantId = tenant };
            new DeleteStreamOperation(Options.EventGraph, streamId, tenant).ConfigureCommand(builder, tenant);

            await using var batch = builder.Compile();
            batch.Connection = conn;
            batch.Transaction = tx;
            await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
