using System;
using System.Linq;
using Baseline;
using Marten.Services;
using Marten.Storage;

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

        private readonly IConnectionFactory _factory;
        private readonly DocumentStore _store;
        private readonly ITenant _tenant;

        public DocumentCleaner(IConnectionFactory factory, DocumentStore store, ITenant tenant)
        {
            _factory = factory;
            _store = store;
            _tenant = tenant;
        }

        public void DeleteAllDocuments()
        {
            var dbObjects = new DbObjects(_factory, _store.Storage);

            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
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
            var mapping = _store.Storage.MappingFor(documentType).As<IDocumentMapping>();
            mapping.DeleteAllDocuments(_factory);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var documentMappings = _store.Storage.AllDocumentMappings.Where(x => !documentTypes.Contains(x.DocumentType));
            foreach (var mapping in documentMappings)
            {
                mapping.As<IDocumentMapping>().DeleteAllDocuments(_factory);
            }
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _store.Storage.MappingFor(documentType);

            using (var conn = _factory.Create())
            {
                conn.Open();

                mapping.RemoveAllObjects(_store.Options.DdlRules, conn);
            }
        }

        public void CompletelyRemoveAll()
        {
            var dbObjects = new DbObjects(_factory, _store.Storage);

            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
            {
                var schemaTables = dbObjects.SchemaTables();
                schemaTables
                    .Each(tableName => { connection.Execute($"DROP TABLE IF EXISTS {tableName} CASCADE;"); });

                var drops = connection.GetStringList(DropAllFunctionSql, new object[] { _store.Storage.AllSchemaNames() });
                drops.Each(drop => connection.Execute(drop));
                connection.Commit();

                _tenant.ResetSchemaExistenceChecks();
            }
        }

        public void DeleteAllEventData()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
            {
                connection.Execute($"truncate table {_store.Events.DatabaseSchemaName}.mt_events cascade;" +
                                   $"truncate table {_store.Events.DatabaseSchemaName}.mt_streams cascade");
                connection.Commit();
            }
        }
    }
}