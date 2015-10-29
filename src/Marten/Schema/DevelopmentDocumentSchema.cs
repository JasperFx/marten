using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Marten.Generation;

namespace Marten.Schema
{
    public class DevelopmentDocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly CommandRunner _runner;
        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes = new ConcurrentDictionary<Type, IDocumentStorage>(); 

        public DevelopmentDocumentSchema(IConnectionFactory connections)
        {
            _runner = new CommandRunner(connections);
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var storage = DocumentStorageBuilder.Build(type);

                if (!DocumentTables().Contains(storage.TableName))
                {
                    buildSchemaObjectsForDocumentType(storage);
                }

                return storage;
            });
        }

        private void buildSchemaObjectsForDocumentType(IDocumentStorage storage)
        {
            var builder = new SchemaBuilder();
            storage.InitializeSchema(builder);

            _runner.Execute(builder.ToSql());
        }

        public IEnumerable<string> SchemaTableNames()
        {
            return _runner.Execute(conn =>
            {
                var table = conn.GetSchema("Tables");
                var tables = new List<string>();
                foreach (DataRow row in table.Rows)
                {
                    tables.Add(row[2].ToString());
                }

                return tables.Where(name => name.StartsWith("mt_")).ToArray();
            });
        }

        public string[] DocumentTables()
        {
            return SchemaTableNames().Where(x => x.Contains("_doc_")).ToArray();
        }

        public IEnumerable<string> SchemaFunctionNames()
        {
            return findFunctionNames().ToArray();
        }

        private IEnumerable<string> findFunctionNames()
        {
            return _runner.Execute(conn =>
            {
                var sql = @"
SELECT routine_name
FROM information_schema.routines
WHERE specific_schema NOT IN ('pg_catalog', 'information_schema')
AND type_udt_name != 'trigger';
";

                var command = conn.CreateCommand();
                command.CommandText = sql;

                var list = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list;
            });

        }


        public void Dispose()
        {
        }
    }
}