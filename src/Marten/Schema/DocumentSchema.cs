using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Baseline;
using Marten.Events;
using Marten.Generation;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema.Sequences;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Schema
{
    public class DocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly IConnectionFactory _factory;
        private readonly IMartenLogger _logger;

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes =
            new ConcurrentDictionary<Type, IDocumentStorage>();


        public DocumentSchema(StoreOptions options, IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;

            StoreOptions = options;

            options.AllDocumentMappings.Each(x => _mappings[x.DocumentType] = x);

            Sequences = new SequenceFactory(this, _factory, options, _logger);

            Parser = new MartenExpressionParser(StoreOptions.Serializer(), StoreOptions);

            HandlerFactory = new QueryHandlerFactory(this);
        }

        public MartenExpressionParser Parser { get; }

        public StoreOptions StoreOptions { get; }


        public void Dispose()
        {
        }

        public IDocumentMapping MappingFor(Type documentType)
        {
            return _mappings.GetOrAdd(documentType, type =>
            {
                if (type == typeof (EventStream))
                {
                    return StoreOptions.Events.As<IDocumentMapping>();
                }

                return
                    StoreOptions.Events.AllEvents().FirstOrDefault(x => x.DocumentType == type)
                    ?? StoreOptions.AllDocumentMappings.SelectMany(x => x.SubClasses)
                        .FirstOrDefault(x => x.DocumentType == type) as IDocumentMapping
                    ?? StoreOptions.MappingFor(type);
            });
        }

        public void EnsureStorageExists(Type documentType)
        {
            // TODO -- HACK! Do something later that's more systematic
            if (documentType == typeof(StreamState)) return;

            StorageFor(documentType);
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var mapping = MappingFor(documentType);
                if (mapping is IDocumentStorage)
                {
                    buildSchemaObjectsIfNecessary(mapping);
                    return mapping.As<IDocumentStorage>();
                }


                assertNoDuplicateDocumentAliases();

                IDocumentStorage storage = null;

                var prebuiltType = StoreOptions.PreBuiltStorage
                    .FirstOrDefault(x => x.DocumentTypeForStorage() == documentType);

                storage = prebuiltType != null ? DocumentStorageBuilder.BuildStorageObject(this, prebuiltType, mapping.As<DocumentMapping>()) : mapping.BuildStorage(this);

                buildSchemaObjectsIfNecessary(mapping);

                return storage;
            });
        }

        private void buildSchemaObjectsIfNecessary(IDocumentMapping mapping)
        {
            Action<string> executeSql = sql =>
            {
                try
                {
                    _factory.RunSql(sql);
                    _logger.SchemaChange(sql);
                }
                catch (Exception e)
                {
                    throw new MartenSchemaException(mapping.DocumentType, sql, e);
                }
            };

            var sortedMappings = new[] {mapping}.TopologicalSort(x =>
            {
                var documentMapping = x as DocumentMapping;
                if (documentMapping == null)
                {
                    return Enumerable.Empty<IDocumentMapping>();
                }

                return documentMapping.ForeignKeys
                    .Select(keyDefinition => keyDefinition.ReferenceDocumentType)
                    .Select(MappingFor);
            });

            sortedMappings.Each(x => x.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, executeSql));
        }

        private void assertNoDuplicateDocumentAliases()
        {
            var duplicates = StoreOptions.AllDocumentMappings.GroupBy(x => x.Alias).Where(x => x.Count() > 1).ToArray();
            if (duplicates.Any())
            {
                var message = duplicates.Select(group =>
                {
                    return
                        $"Document types {@group.Select(x => x.DocumentType.Name).Join(", ")} all have the same document alias '{@group.Key}'. You must explicitly make document type aliases to disambiguate the database schema objects";
                }).Join("\n");

                throw new AmbiguousDocumentTypeAliasesException(message);
            }
        }

        public IEventStoreConfiguration Events => StoreOptions.Events;
        public PostgresUpsertType UpsertType => StoreOptions.UpsertType;

        public TableName[] SchemaTables()
        {
            Func<DbDataReader, TableName> transform = r => new TableName(r.GetString(0), r.GetString(1));

            var sql = "select table_schema, table_name from information_schema.tables where table_name like ? and table_schema = ANY(?);";

            var schemaNames = AllSchemaNames();

            var tablePattern = DocumentMapping.MartenPrefix + "%";
            var tables = _factory.Fetch(sql, transform, tablePattern, schemaNames).ToArray();


            return tables;


        }

        public string[] AllSchemaNames()
        {
            var schemas =
                AllDocumentMaps().OfType<DocumentMapping>().Select(x => x.DatabaseSchemaName).Distinct().ToList();

            schemas.Fill(StoreOptions.DatabaseSchemaName);
            schemas.Fill(StoreOptions.Events.DatabaseSchemaName);

            var schemaNames = schemas.Select(x => x.ToLowerInvariant()).ToArray();
            return schemaNames;
        }

        public TableName[] DocumentTables()
        {
            return SchemaTables().Where(x => x.Name.StartsWith(DocumentMapping.TablePrefix)).ToArray();
        }

        public FunctionName[] SchemaFunctionNames()
        {
            Func<DbDataReader, FunctionName> transform = r => new FunctionName(r.GetString(0), r.GetString(1));

            var sql = "SELECT specific_schema, routine_name FROM information_schema.routines WHERE type_udt_name != 'trigger' and routine_name like ? and specific_schema = ANY(?);";

            return _factory.Fetch(sql, transform, DocumentMapping.MartenPrefix + "%", AllSchemaNames()).ToArray();
        }

        public ISequences Sequences { get; }

        public void WriteDDL(string filename)
        {
            var sql = ToDDL();
            new FileSystem().WriteStringToFile(filename, sql);
        }

        public void WriteDDLByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            WriteDatabaseSchemaGenerationScript(directory, system);

            _mappings.Values.Where(x => !(x is SubClassMapping)).Each(mapping =>
            {
                var writer = new StringWriter();
                mapping.WriteSchemaObjects(this, writer);

                var filename = directory.AppendPath(mapping.Alias + ".sql");
                system.WriteStringToFile(filename, writer.ToString());
            });

            var hiloScript = getHiloScript();
            system.WriteStringToFile(directory.AppendPath("mt_hilo.sql"), hiloScript);

            if (Events.IsActive)
            {
                var filename = directory.AppendPath("mt_streams.sql");
                var writer = new StringWriter();

                Events.As<IDocumentMapping>().WriteSchemaObjects(this, writer);

                system.WriteStringToFile(filename, writer.ToString());
            }
        }

        private void WriteDatabaseSchemaGenerationScript(string directory, FileSystem system)
        {
            var allSchemaNames = AllSchemaNames();
            var script = DatabaseSchemaGenerator.GenerateScript(allSchemaNames);

            if (script.IsNotEmpty())
            {
                var filename = directory.AppendPath("database_schemas.sql");
                system.WriteStringToFile(filename, script);
            }
        }

        private string getHiloScript()
        {
            return SchemaBuilder.GetSqlScript(StoreOptions.DatabaseSchemaName, "mt_hilo");
        }

        public string ToDDL()
        {
            var writer = new StringWriter();

            var allSchemaNames = AllSchemaNames();
            DatabaseSchemaGenerator.WriteSql(allSchemaNames, writer);

            StoreOptions.AllDocumentMappings.Each(x => x.WriteSchemaObjects(this, writer));

            if (Events.IsActive)
            {
                Events.As<IDocumentMapping>().WriteSchemaObjects(this, writer);
            }

            writer.WriteLine(SchemaBuilder.GetSqlScript(StoreOptions.DatabaseSchemaName, "mt_hilo"));

            return writer.ToString();
        }

        public TableDefinition TableSchema(IDocumentMapping documentMapping)
        {
            var columns = findTableColumns(documentMapping);
            if (!columns.Any()) return null;

            var pkName = primaryKeysFor(documentMapping).SingleOrDefault();

            return new TableDefinition(documentMapping.Table, pkName,  columns);
        }

        public TableDefinition TableSchema(Type documentType)
        {
            return TableSchema(MappingFor(documentType));
        }

        public IEnumerable<IDocumentMapping> AllDocumentMaps()
        {
            return StoreOptions.AllDocumentMappings;
        }

        public IResolver<T> ResolverFor<T>()
        {
            return StorageFor(typeof (T)).As<IResolver<T>>();
        }

        public bool TableExists(TableName table)
        {
            var schemaTables = SchemaTables();
            return schemaTables.Contains(table);
        }

        public IQueryHandlerFactory HandlerFactory { get; }
        public void ResetSchemaExistenceChecks()
        {
            AllDocumentMaps().Each(x => x.ResetSchemaExistenceChecks());
            Events.As<EventGraph>().ResetSchemaExistenceChecks();

            _documentTypes.Clear();
        }

        private string[] primaryKeysFor(IDocumentMapping documentMapping)
        {
            var sql = @"
select a.attname, format_type(a.atttypid, a.atttypmod) as data_type
from pg_index i
join   pg_attribute a on a.attrelid = i.indrelid and a.attnum = ANY(i.indkey)
where attrelid = (select pg_class.oid 
                  from pg_class 
                  join pg_catalog.pg_namespace n ON n.oid = pg_class.relnamespace
                  where n.nspname = ? and relname = ?)
and i.indisprimary; 
";

            return _factory.GetStringList(sql, documentMapping.Table.Schema, documentMapping.Table.Name).ToArray();
        }

        private IEnumerable<TableColumn> findTableColumns(IDocumentMapping documentMapping)
        {
            Func<DbDataReader, TableColumn> transform = r => new TableColumn(r.GetString(0), r.GetString(1));

            var sql = "select column_name, data_type from information_schema.columns where table_schema = ? and table_name = ? order by ordinal_position";

            return _factory.Fetch(sql, transform, documentMapping.Table.Schema, documentMapping.Table.Name);
        }
    }

    [Serializable]
    public class AmbiguousDocumentTypeAliasesException : Exception
    {
        public AmbiguousDocumentTypeAliasesException(string message) : base(message)
        {
        }

        protected AmbiguousDocumentTypeAliasesException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}