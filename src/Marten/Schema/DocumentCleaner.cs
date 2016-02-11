using System;
using System.Linq;
using Baseline;
using Marten.Services;

namespace Marten.Schema
{
    public class DocumentCleaner : IDocumentCleaner
    {
        private readonly string _dropAllFunctionSql = @"
SELECT format('DROP FUNCTION %s(%s);'
             ,oid::regproc
             ,pg_get_function_identity_arguments(oid))
FROM   pg_proc
WHERE  proname like 'mt_%' 
AND    pg_function_is_visible(oid)  
                                  
";

        private readonly string _dropFunctionSql = @"
SELECT format('DROP FUNCTION %s(%s);'
             ,oid::regproc
             ,pg_get_function_identity_arguments(oid))
FROM   pg_proc
WHERE  proname = '{0}' 
AND    pg_function_is_visible(oid)  
                                  
";
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
            var tableName = _schema.MappingFor(documentType).TableName;
            var sql = "truncate {0} cascade".ToFormat(tableName);
            _factory.RunSql(sql);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var exemptedTables = documentTypes.Select(x => _schema.MappingFor(x).TableName).ToArray();
            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                _schema.DocumentTables().Where(x => !exemptedTables.Contains(x)).Each(tableName =>
                {
                    var sql = "truncate {0} cascade".ToFormat(tableName);
                    conn.Execute(sql);
                });
            }
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);


            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                connection.Execute($"DROP TABLE IF EXISTS {mapping.TableName} CASCADE;");

                var dropTargets = _dropFunctionSql.ToFormat(mapping.UpsertName);

                var drops = connection.GetStringList(dropTargets);
                drops.Each(drop => connection.Execute(drop));
            }
        }


        public void CompletelyRemoveAll()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                _schema.SchemaTableNames()
                    .Each(tableName => { connection.Execute($"DROP TABLE IF EXISTS {tableName} CASCADE;"); });


                var drops = connection.GetStringList(_dropAllFunctionSql);
                drops.Each(drop => connection.Execute(drop));
            }
        }
    }
}