using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Services;
using Marten.Util;
using Npgsql;

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
            _schema.DocumentTables().Each(truncateTable);
        }

        public void DeleteDocumentsFor(Type documentType)
        {
            var tableName = _schema.MappingFor(documentType).TableName;
            truncateTable(tableName);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var exemptedTables = documentTypes.Select(x => _schema.MappingFor(x).TableName).ToArray();
            _schema.DocumentTables().Where(x => !exemptedTables.Contains(x)).Each(truncateTable);
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);

            dropTable(mapping.TableName);

            var dropTargets = _dropFunctionSql.ToFormat(mapping.UpsertName);
            dropFunctions(dropTargets);
        }


        public void CompletelyRemoveAll()
        {
            _schema.SchemaTableNames().Each(dropTable);

            dropFunctions(_dropAllFunctionSql);
        }

        private void truncateTable(string tableName)
        {
            var sql = "truncate {0} cascade".ToFormat(tableName);
            _factory.RunSql(sql);
        }

        private void dropFunctions(string dropTargets)
        {
            var drops = _factory.GetStringList(dropTargets);

            drops.Each(drop => _factory.RunSql(drop));
        }

        private void dropTable(string tableName)
        {
            _factory.RunSql($"DROP TABLE IF EXISTS {tableName} CASCADE;");
        }
    }
}