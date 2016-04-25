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
WHERE  p.proname like 'mt_%'";

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
        private readonly IDocumentSchema _schema;

        public DocumentCleaner(IConnectionFactory factory, IDocumentSchema schema)
        {
            _factory = factory;
            _schema = schema;
        }

        public void DeleteAllDocuments()
        {
            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                _schema.DocumentTables().Each(tableName =>
                {
                    var sql = "truncate {0} cascade".ToFormat(tableName);
                    conn.Execute(sql);
                });
            }
        }

        public void DeleteDocumentsFor(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);
            mapping.DeleteAllDocuments(_factory);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            _schema.AllDocumentMaps().Where(x => !documentTypes.Contains(x.DocumentType)).Each(x =>
            {
                x.DeleteAllDocuments(_factory);
            });
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);

            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                mapping.RemoveSchemaObjects(connection);
            }
        }

        public void CompletelyRemoveAll()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                _schema.SchemaTables()
                    .Each(tableName => { connection.Execute($"DROP TABLE IF EXISTS {tableName} CASCADE;"); });

                var drops = connection.GetStringList(DropAllFunctionSql);
                drops.Each(drop => connection.Execute(drop));

                _schema.ResetSchemaExistenceChecks();
            }
        }

        public void DeleteAllEventData()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                connection.Execute($"truncate table {_schema.StoreOptions.DatabaseSchemaName}.mt_events cascade;" +
                                   $"truncate table {_schema.StoreOptions.DatabaseSchemaName}.mt_streams cascade");
            }
        }
    }
}