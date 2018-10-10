using System;
using System.Linq;
using Baseline;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Schema
{
    public class DocumentCleaner : IDocumentCleaner
    {
        public static string DropAllFunctionSql = @"
SELECT format('DROP FUNCTION %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE  p.proname like 'mt_%' and n.nspname = ANY(?)";

        public static readonly string DropFunctionSql = @"
SELECT format('DROP FUNCTION %s.%s(%s);'
             ,n.nspname
             ,p.proname
             ,pg_get_function_identity_arguments(p.oid))
FROM   pg_proc p
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE  p.proname = '{0}'
AND    n.nspname = '{1}';";

        private readonly StoreOptions _options;
        private readonly ITenant _tenant;

        public DocumentCleaner(StoreOptions options, ITenant tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        public void DeleteAllDocuments()
        {
            var dbObjects = new DbObjects(_tenant, _options.Storage);

            using (var conn = _tenant.OpenConnection(CommandRunnerMode.Transactional))
            {
                dbObjects.DocumentTables().Each(tableName =>
                {
                    var sql = "truncate {0} cascade".ToFormat(tableName);
                    conn.Execute(sql);
                });
                conn.Commit();
            }
        }

        public void DeleteDocumentsFor(Type documentType)
        {
            var mapping = _options.Storage.FindMapping(documentType);
            mapping.DeleteAllDocuments(_tenant);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var documentMappings = _options.Storage.AllDocumentMappings.Where(x => !documentTypes.Contains(x.DocumentType));
            foreach (var mapping in documentMappings)
            {
                mapping.As<IDocumentMapping>().DeleteAllDocuments(_tenant);
            }
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _options.Storage.MappingFor(documentType);

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                mapping.RemoveAllObjects(_options.DdlRules, conn);
            }
        }

        public void CompletelyRemoveAll()
        {
            var dbObjects = new DbObjects(_tenant, _options.Storage);

            using (var connection = _tenant.OpenConnection(CommandRunnerMode.Transactional))
            {
                var schemaTables = dbObjects.SchemaTables();
                schemaTables
                    .Each(tableName => { connection.Execute($"DROP TABLE IF EXISTS {tableName} CASCADE;"); });

                var drops = connection.GetStringList(DropAllFunctionSql, new object[] { _options.Storage.AllSchemaNames() });
                drops.Each(drop => connection.Execute(drop));
                connection.Commit();

                _tenant.ResetSchemaExistenceChecks();
            }
        }

        public void DeleteAllEventData()
        {
            using (var connection = _tenant.OpenConnection(CommandRunnerMode.Transactional))
            {
                connection.Execute($"truncate table {_options.Events.DatabaseSchemaName}.mt_events cascade;" +
                                   $"truncate table {_options.Events.DatabaseSchemaName}.mt_streams cascade");
                connection.Commit();
            }
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
            if (typeof(T) != _options.Events.GetStreamIdType())
            {
                throw new ArgumentException($"{nameof(streamId)} should  be of type {_options.Events.GetStreamIdType()}", nameof(streamId));
            }

            using (var conn = _tenant.CreateConnection())
            {
                var streamsWhere = "id = :id";
                var eventsWhere = "stream_id = :id";

                if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
                {
                    var tenantPart = $" AND tenant_id = :tenantId";
                    streamsWhere += tenantPart;
                    eventsWhere += tenantPart;
                }

                var cmd = conn.CreateCommand().WithText($"delete from {_options.Events.DatabaseSchemaName}.mt_events where {eventsWhere};delete from {_options.Events.DatabaseSchemaName}.mt_streams where {streamsWhere}");
                cmd.AddNamedParameter("id", streamId);

                if (_options.Events.TenancyStyle == TenancyStyle.Conjoined)
                {
                    cmd.AddNamedParameter("tenantId", _tenant.TenantId);
                }

                conn.Open();

                cmd.ExecuteNonQuery();
            }
        }
    }
}