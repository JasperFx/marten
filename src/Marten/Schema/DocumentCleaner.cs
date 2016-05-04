using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;

namespace Marten.Schema
{
    public class DocumentCleaner : IDocumentCleaner
    {
        private const string DropIfExists = "DROP TABLE IF EXISTS {0} CASCADE;";
        private const string TruncateCascade = "truncate {0} cascade";
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
        private readonly string TruncateEventsCascade;

        public DocumentCleaner(IConnectionFactory factory, DocumentSchema schema)
        {
            _factory = factory;
            _schema = schema;
            TruncateEventsCascade = $"truncate table {_schema.Events.DatabaseSchemaName}.mt_events cascade;" +
                                    $"truncate table {_schema.Events.DatabaseSchemaName}.mt_streams cascade";
        }

        public void DeleteAllDocuments()
        {
            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                foreach (var tableName in _schema.DbObjects.DocumentTables())
                {
                    var sql = TruncateCascade.ToFormat(tableName);
                    conn.Execute(sql);
                }
            }
        }

        public async Task DeleteAllDocumentsAsync(CancellationToken token)
        {
            using (var conn = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                foreach (var tableName in _schema.DbObjects.DocumentTables())
                {
                    var sql = TruncateCascade.ToFormat(tableName);
                    await conn.ExecuteAsync(sql, token).ConfigureAwait(false);
                }
            }
        }

        public void DeleteDocumentsFor(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);
            mapping.DeleteAllDocuments(_factory);
        }

        public Task DeleteDocumentsForAsync(Type documentType, CancellationToken token)
        {
            var mapping = _schema.MappingFor(documentType);
            return mapping.DeleteAllDocumentsAsync(_factory, token);
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            foreach (var mapping in _schema.AllDocumentMaps().Where(x => !documentTypes.Contains(x.DocumentType)))
            {
                mapping.DeleteAllDocuments(_factory);
            }
        }

        public async Task DeleteDocumentsExceptAsync(CancellationToken token, params Type[] documentTypes)
        {
            foreach (var mapping in _schema.AllDocumentMaps().Where(x => !documentTypes.Contains(x.DocumentType)))
            {
                await mapping.DeleteAllDocumentsAsync(_factory, token).ConfigureAwait(false);
            }
        }

        public void CompletelyRemove(Type documentType)
        {
            var mapping = _schema.MappingFor(documentType);

            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                mapping.RemoveSchemaObjects(connection);
            }
        }

        public async Task CompletelyRemoveAsync(Type documentType, CancellationToken token)
        {
            var mapping = _schema.MappingFor(documentType);

            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                await mapping.RemoveSchemaObjectsAsync(connection, token).ConfigureAwait(false);
            }
        }

        public void CompletelyRemoveAll()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                var schemaTables = _schema.DbObjects.SchemaTables();
                foreach (var tableName in schemaTables)
                {
                    connection.Execute(DropIfExists.ToFormat(tableName));
                }

                foreach (var drop in connection.GetStringList(DropAllFunctionSql, new object[] { _schema.AllSchemaNames() }))
                {
                    connection.Execute(drop);
                }
                _schema.ResetSchemaExistenceChecks();
            }
        }

        public async Task CompletelyRemoveAllAsync(CancellationToken token)
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                var schemaTables = _schema.DbObjects.SchemaTables();
                foreach (var tableName in schemaTables)
                {
                    await connection.ExecuteAsync(DropIfExists.ToFormat(tableName), token).ConfigureAwait(false);
                }

                foreach (var drop in connection.GetStringList(DropAllFunctionSql, new object[] { _schema.AllSchemaNames() }))
                {
                    await connection.ExecuteAsync(drop, token).ConfigureAwait(false);
                }
                _schema.ResetSchemaExistenceChecks();
            }
        }

        public void DeleteAllEventData()
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                connection.Execute(TruncateEventsCascade);
            }
        }

        public async Task DeleteAllEventDataAsync(CancellationToken token)
        {
            using (var connection = new ManagedConnection(_factory, CommandRunnerMode.ReadOnly))
            {
                await connection.ExecuteAsync(TruncateEventsCascade, token: token).ConfigureAwait(false);
            }
        }
    }
}