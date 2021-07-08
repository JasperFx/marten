using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Exceptions;
using Marten.Internal;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Weasel.Core;

namespace Marten.Schema
{
    public class DocumentCleaner: IDocumentCleaner
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

        private readonly StoreOptions _options;
        private readonly ITenant _tenant;

        public DocumentCleaner(StoreOptions options, ITenant tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        public void DeleteAllDocuments()
        {
            DeleteAllDocumentsAsync().GetAwaiter().GetResult();
        }

        public async Task DeleteAllDocumentsAsync()
        {
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync();

            var schemas = _options.Storage.AllSchemaNames();
            var tables = await conn.ExistingTables("mt_%", schemas);

            if (!tables.Any()) return;

            var builder = new CommandBuilder();
            foreach (var table in tables)
            {
                builder.Append($"truncate {table} cascade;");
            }

            await builder.ExecuteNonQueryAsync(conn);
        }

        public void DeleteDocumentsByType(Type documentType)
        {
            var storage = _options.Tenancy.Default.Providers.StorageFor(documentType);
            storage.TruncateDocumentStorage(_tenant);
        }

        public Task DeleteDocumentsByTypeAsync(Type documentType)
        {
            var storage = _options.Tenancy.Default.Providers.StorageFor(documentType);
            return storage.TruncateDocumentStorageAsync(_tenant);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var documentMappings = _options.Storage.AllDocumentMappings.Where(x => !documentTypes.Contains(x.DocumentType));
            foreach (var mapping in documentMappings)
            {
                var storage = _options.Tenancy.Default.Providers.StorageFor(mapping.DocumentType);
                storage.TruncateDocumentStorage(_tenant);
            }
        }

        public async Task DeleteDocumentsExceptAsync(params Type[] documentTypes)
        {
            var documentMappings = _options.Storage.AllDocumentMappings.Where(x => !documentTypes.Contains(x.DocumentType));
            foreach (var mapping in documentMappings)
            {
                var storage = _options.Tenancy.Default.Providers.StorageFor(mapping.DocumentType);
                await storage.TruncateDocumentStorageAsync(_tenant);
            }
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _options.Storage.MappingFor(documentType);
            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                var writer = new StringWriter();
                foreach (var schemaObject in ((IFeatureSchema) mapping.Schema).Objects)
                {
                    schemaObject.WriteDropStatement(_options.Advanced.DdlRules, writer);
                }

                var sql = writer.ToString();
                var cmd = conn.CreateCommand(sql);

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception e)
                {
                    MartenExceptionTransformer.WrapAndThrow(cmd, e);
                }
            }
        }

        public async Task CompletelyRemoveAsync(Type documentType)
        {
            var mapping = _options.Storage.MappingFor(documentType);
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync();

            var writer = new StringWriter();
            foreach (var schemaObject in ((IFeatureSchema) mapping.Schema).Objects)
            {
                schemaObject.WriteDropStatement(_options.Advanced.DdlRules, writer);
            }

            var sql = writer.ToString();
            var cmd = conn.CreateCommand(sql);

            try
            {
                await cmd.ExecuteNonQueryAsync();
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

        public async Task CompletelyRemoveAllAsync()
        {
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync();
            var schemas = _options.Storage.AllSchemaNames();
            var tables = await conn.ExistingTables("mt_%", schemas);

            var builder = new CommandBuilder();


            foreach (var table in tables)
            {
                builder.Append($"DROP TABLE IF EXISTS {table} CASCADE;");
            }

            var functionDrops = await conn.CreateCommand(DropAllFunctionSql)
                .With("schemas", schemas)
                .FetchList<string>();
            foreach (var functionDrop in functionDrops)
            {
                builder.Append(functionDrop);
            }

            var sequenceDrops = await conn.CreateCommand(DropAllSequencesSql)
                .With("schemas", schemas)
                .FetchList<string>();
            foreach (var sequenceDrop in sequenceDrops)
            {
                builder.Append(sequenceDrop);
            }

            if (tables.Any() || functionDrops.Any() || sequenceDrops.Any())
            {
                await builder.ExecuteNonQueryAsync(conn);
            }

            _tenant.ResetSchemaExistenceChecks();
        }

        public void DeleteAllEventData()
        {
            using var connection = _tenant.OpenConnection(CommandRunnerMode.Transactional);
            var deleteEventDataSql = toDeleteEventDataSql();
            connection.Execute(deleteEventDataSql);
            connection.Commit();
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

        public async Task DeleteAllEventDataAsync()
        {
            using var connection = _tenant.CreateConnection();
            await connection.OpenAsync();
#if NET5_0
            var tx = await connection.BeginTransactionAsync();
            #else
            var tx = connection.BeginTransaction();
#endif
            var deleteEventDataSql = toDeleteEventDataSql();
            await connection.CreateCommand(deleteEventDataSql, tx).ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        public void DeleteSingleEventStream(Guid streamId)
        {
            DeleteSingleEventStream<Guid>(streamId);
        }

        public void DeleteSingleEventStream(string streamId)
        {
            DeleteSingleEventStream<string>(streamId);
        }

        private void DeleteSingleEventStream<T>(T streamId)
        {
            if (typeof(T) != _options.EventGraph.GetStreamIdType())
            {
                throw new ArgumentException($"{nameof(streamId)} should  be of type {_options.EventGraph.GetStreamIdType()}", nameof(streamId));
            }

            using var conn = _tenant.CreateConnection();
            var streamsWhere = "id = :id";
            var eventsWhere = "stream_id = :id";

            if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
            {
                var tenantPart = $" AND tenant_id = :tenantId";
                streamsWhere += tenantPart;
                eventsWhere += tenantPart;
            }

            var cmd = conn.CreateCommand($"delete from {_options.Events.DatabaseSchemaName}.mt_events where {eventsWhere};delete from {_options.Events.DatabaseSchemaName}.mt_streams where {streamsWhere}");
            cmd.AddNamedParameter("id", streamId);

            if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
            {
                cmd.AddNamedParameter("tenantId", _tenant.TenantId);
            }

            conn.Open();

            cmd.ExecuteNonQuery();
        }

        public Task DeleteSingleEventStreamAsync(Guid streamId)
        {
            return DeleteSingleEventStreamAsync<Guid>(streamId);
        }

        public Task DeleteSingleEventStreamAsync(string streamId)
        {
            return DeleteSingleEventStreamAsync<string>(streamId);
        }

        private async Task DeleteSingleEventStreamAsync<T>(T streamId)
        {
            if (typeof(T) != _options.EventGraph.GetStreamIdType())
            {
                throw new ArgumentException($"{nameof(streamId)} should  be of type {_options.EventGraph.GetStreamIdType()}", nameof(streamId));
            }

            using var conn = _tenant.CreateConnection();
            var streamsWhere = "id = :id";
            var eventsWhere = "stream_id = :id";

            if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
            {
                var tenantPart = $" AND tenant_id = :tenantId";
                streamsWhere += tenantPart;
                eventsWhere += tenantPart;
            }

            var cmd = conn.CreateCommand($"delete from {_options.Events.DatabaseSchemaName}.mt_events where {eventsWhere};delete from {_options.Events.DatabaseSchemaName}.mt_streams where {streamsWhere}");
            cmd.AddNamedParameter("id", streamId);

            if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
            {
                cmd.AddNamedParameter("tenantId", _tenant.TenantId);
            }

            await conn.OpenAsync();

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
