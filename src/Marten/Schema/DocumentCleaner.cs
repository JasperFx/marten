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
            using (var conn = _tenant.CreateConnection())
            {
                var cmd = conn.CreateCommand().WithText($"delete from {_options.Events.DatabaseSchemaName}.mt_events where stream_id = :id;delete from {_options.Events.DatabaseSchemaName}.mt_streams where id = :id");
                cmd.AddNamedParameter("id", streamId);

                conn.Open();

                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteSingleEventStream(string streamId)
        {
            using (var conn = _tenant.CreateConnection())
            {
                var cmd = conn.CreateCommand().WithText($"delete from {_options.Events.DatabaseSchemaName}.mt_events where stream_id = :id;delete from {_options.Events.DatabaseSchemaName}.mt_streams where id = :id");
                cmd.AddNamedParameter("id", streamId);

                conn.Open();

                cmd.ExecuteNonQuery();
            }
        }
    }
}