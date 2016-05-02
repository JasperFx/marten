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

namespace Marten.Schema
{
    public class DocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes =
            new ConcurrentDictionary<Type, IDocumentStorage>();

        private readonly IConnectionFactory _factory;
        private readonly IMartenLogger _logger;

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();


        public DocumentSchema(StoreOptions options, IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;

            StoreOptions = options;

            options.AllDocumentMappings.Each(x => _mappings[x.DocumentType] = x);

            Sequences = new SequenceFactory(this, _factory, options, _logger);

            Parser = new MartenExpressionParser(StoreOptions.Serializer(), StoreOptions);

            HandlerFactory = new QueryHandlerFactory(this);

            DbObjects = new DbObjects(_factory, this);
        }

        public PostgresUpsertType UpsertType => StoreOptions.UpsertType;


        public void Dispose()
        {
        }

        public IDbObjects DbObjects { get; }

        public MartenExpressionParser Parser { get; }

        public StoreOptions StoreOptions { get; }

        public IDocumentMapping MappingFor(Type documentType)
        {
            return _mappings.GetOrAdd(documentType, type =>
            {
                if (type == typeof(EventStream))
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

                storage = prebuiltType != null
                    ? DocumentStorageBuilder.BuildStorageObject(this, prebuiltType, mapping.As<DocumentMapping>())
                    : mapping.BuildStorage(this);

                buildSchemaObjectsIfNecessary(mapping);

                return storage;
            });
        }

        public IEventStoreConfiguration Events => StoreOptions.Events;



        public string[] AllSchemaNames()
        {
            var schemas =
                AllDocumentMaps().OfType<DocumentMapping>().Select(x => x.DatabaseSchemaName).Distinct().ToList();

            schemas.Fill(StoreOptions.DatabaseSchemaName);
            schemas.Fill(StoreOptions.Events.DatabaseSchemaName);

            var schemaNames = schemas.Select(x => x.ToLowerInvariant()).ToArray();
            return schemaNames;
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


        public IEnumerable<IDocumentMapping> AllDocumentMaps()
        {
            return StoreOptions.AllDocumentMappings;
        }

        public IResolver<T> ResolverFor<T>()
        {
            return StorageFor(typeof(T)).As<IResolver<T>>();
        }


        public IQueryHandlerFactory HandlerFactory { get; }

        public void ResetSchemaExistenceChecks()
        {
            AllDocumentMaps().Each(x => x.ResetSchemaExistenceChecks());
            Events.As<EventGraph>().ResetSchemaExistenceChecks();

            _documentTypes.Clear();
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

            sortedMappings.Each(
                x => x.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, executeSql));
        }

        private void assertNoDuplicateDocumentAliases()
        {
            var duplicates = StoreOptions.AllDocumentMappings.GroupBy(x => x.Alias).Where(x => x.Count() > 1).ToArray();
            if (duplicates.Any())
            {
                var message = duplicates.Select(group =>
                {
                    return
                        $"Document types {group.Select(x => x.DocumentType.Name).Join(", ")} all have the same document alias '{group.Key}'. You must explicitly make document type aliases to disambiguate the database schema objects";
                }).Join("\n");

                throw new AmbiguousDocumentTypeAliasesException(message);
            }
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