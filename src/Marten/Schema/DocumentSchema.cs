using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Marten.Events;
using Marten.Schema.Sequences;
using Marten.Services;

namespace Marten.Schema
{
    public class DocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly IDocumentSchemaCreation _creation;
        private readonly ICommandRunner _runner;
        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes = new ConcurrentDictionary<Type, IDocumentStorage>(); 
        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings = new ConcurrentDictionary<Type, DocumentMapping>(); 

        public DocumentSchema(ICommandRunner runner, IDocumentSchemaCreation creation)
        {
            _creation = creation;
            _runner = runner;

            Sequences = new SequenceFactory(this, _runner, _creation);

            Events = new EventGraph();
        }

        public DocumentMapping MappingFor(Type documentType)
        {
            return _documentMappings.GetOrAdd(documentType, type => new DocumentMapping(type));
        }

        public void EnsureStorageExists(Type documentType)
        {
            StorageFor(documentType);
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var mapping = MappingFor(documentType);
                var storage = DocumentStorageBuilder.Build(this, mapping);

                if (!DocumentTables().Contains(mapping.TableName))
                {
                    _creation.CreateSchema(this, mapping);
                }

                return storage;
            });
        }

        public EventGraph Events { get; private set; }

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
            return SchemaTableNames().Where(x => x.StartsWith("mt_doc_")).ToArray();
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

        public void Alter(Action<MartenRegistry> configure)
        {
            var registry = new MartenRegistry();
            configure(registry);

            Alter(registry);
        }

        public void Alter<T>() where T : MartenRegistry, new()
        {
            Alter(new T());
        }

        public void Alter(MartenRegistry registry)
        {
            // TODO -- later, latch on MartenRegistry type? May not really matter
            registry.Alter(this);
        }

        public ISequences Sequences { get; }

        public PostgresUpsertType UpsertType { get; set; } = PostgresUpsertType.Legacy;
    }
}