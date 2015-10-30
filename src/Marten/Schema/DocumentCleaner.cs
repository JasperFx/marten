using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using FubuCore.CommandLine;
using Marten.Generation;
using Npgsql;

namespace Marten.Schema
{
    public class DocumentCleaner : IDocumentCleaner
    {
        private readonly IDocumentSchema _schema;
        private readonly CommandRunner _runner;

        public DocumentCleaner(IConnectionFactory factory, IDocumentSchema schema)
        {
            _schema = schema;
            _runner = new CommandRunner(factory);
        }

        public void DeleteAllDocuments()
        {
            _schema.DocumentTables().Each(truncateTable);
        }

        public void DocumentsFor(Type documentType)
        {
            var tableName = DocumentMapping.TableNameFor(documentType);
            truncateTable(tableName);
        }

        private void truncateTable(string tableName)
        {
            var sql = "truncate {0} cascade".ToFormat(tableName);

            _runner.Execute(sql);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            var exemptedTables = documentTypes.Select(DocumentMapping.TableNameFor).ToArray();
            _schema.DocumentTables().Where(x => !exemptedTables.Contains(x)).Each(truncateTable);

        }

        public void CompletelyRemove(Type documentType)
        {
            var tableName = DocumentMapping.TableNameFor(documentType);
            dropTable(tableName);

            var dropTargets = _dropFunctionSql.ToFormat(DocumentMapping.UpsertNameFor(documentType));
            dropFunctions(dropTargets);
        }


        public void CompletelyRemoveAll()
        {
            _schema.SchemaTableNames().Each(dropTable);

            dropFunctions(_dropAllFunctionSql);
        }

        private void dropFunctions(string dropTargets)
        {
            var drops = new List<string>();
            _runner.Execute(conn =>
            {
                var cmd = conn.CreateCommand();

                cmd.CommandText = dropTargets;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        drops.Add(reader.GetString(0));
                    }

                    reader.Close();
                }
            });

            drops.Each(drop => { _runner.Execute(drop); });
        }

        private void dropTable(string tableName)
        {
            _runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(tableName));
        }


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
    }


}