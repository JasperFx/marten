using System;
using System.Linq;
using Baseline;
using Marten.Services;

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
        private readonly DocumentSchema _schema;
        private readonly DocumentStore _store;

        public DocumentCleaner(IConnectionFactory factory, DocumentSchema schema, DocumentStore store)
        {
            _factory = factory;
            _schema = schema;
            _store = store;
        }

        public void DeleteAllDocuments()
        {
            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
            {
                _schema.DbObjects.DocumentTables().Each(tableName =>
                {
                    var sql = "truncate {0} cascade".ToFormat(tableName);
                    conn.Execute(sql);
                });
                conn.Commit();
            }
        }

        public void DeleteDocumentsFor(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);
            mapping.DeleteAllDocuments(_factory);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            _schema.AllMappings.Where(x => !documentTypes.Contains(x.DocumentType)).Each(x =>
            {
                x.DeleteAllDocuments(_factory);
            });
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);

            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
            {
                mapping.SchemaObjects.RemoveSchemaObjects(connection);
                connection.Commit();
            }
        }

        public void CompletelyRemoveAll()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.Transactional))
            {
                var schemaTables = _schema.DbObjects.SchemaTables();
                schemaTables
                    .Each(tableName => { connection.Execute($"DROP TABLE IF EXISTS {tableName} CASCADE;"); });

                var drops = connection.GetStringList(DropAllFunctionSql, new object[] { _schema.AllSchemaNames() });
                drops.Each(drop => connection.Execute(drop));
                connection.Commit();

                _schema.ResetSchemaExistenceChecks();
                _schema.RebuildSystemFunctions();
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