using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
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


        private readonly ConcurrentDictionary<Type, object> _bulkLoaders = new ConcurrentDictionary<Type, object>();
        private readonly ConcurrentDictionary<Type, IDocumentUpsert> _upserts = new ConcurrentDictionary<Type, IDocumentUpsert>();
        private readonly ConcurrentDictionary<Type, object> _identityAssignments = new ConcurrentDictionary<Type, object>();


        public DocumentSchema(StoreOptions options, IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;

            StoreOptions = options;

            options.AllDocumentMappings.Each(x => _mappings[x.DocumentType] = x);

            Sequences = new SequenceFactory(this, _factory, options, _logger);

            Parser = new MartenExpressionParser(StoreOptions.Serializer(), StoreOptions);

            HandlerFactory = new QueryHandlerFactory(this, options.Serializer());

            DbObjects = new DbObjects(_factory, this);
        }



        public void Dispose()
        {
        }

        public IDbObjects DbObjects { get; }
        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            EnsureStorageExists(typeof(T));

            return _bulkLoaders.GetOrAdd(typeof(T), t =>
            {
                var assignment = IdAssignmentFor<T>();

                var mapping = MappingFor(typeof(T));

                if (mapping is DocumentMapping)
                {
                    return new BulkLoader<T>(StoreOptions.Serializer(), mapping.As<DocumentMapping>(), assignment);
                }

                
                throw new ArgumentOutOfRangeException("T", "Marten cannot do bulk inserts of " + typeof(T).FullName);
            }).As<IBulkLoader<T>>();
        }

        public IDocumentUpsert UpsertFor(Type documentType)
        {
            EnsureStorageExists(documentType);

            return _upserts.GetOrAdd(documentType, type =>
            {
                return MappingFor(documentType).BuildUpsert(this);
            });
        }

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

            buildSchemaObjectsIfNecessary(MappingFor(documentType));
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

                IDocumentStorage storage = mapping.BuildStorage(this);

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

            _mappings.Values.OfType<DocumentMapping>().Each(mapping =>
            {
                var writer = new StringWriter();
                mapping.SchemaObjects.WriteSchemaObjects(this, writer);

                var filename = directory.AppendPath(mapping.Alias + ".sql");
                system.WriteStringToFile(filename, writer.ToString());
            });

            var hiloScript = getHiloScript();
            system.WriteStringToFile(directory.AppendPath("mt_hilo.sql"), hiloScript);

            if (Events.IsActive)
            {
                var filename = directory.AppendPath("mt_streams.sql");
                var writer = new StringWriter();

                Events.As<IDocumentMapping>().SchemaObjects.WriteSchemaObjects(this, writer);

                system.WriteStringToFile(filename, writer.ToString());
            }
        }


        public string ToDDL()
        {
            var writer = new StringWriter();

            var allSchemaNames = AllSchemaNames();
            DatabaseSchemaGenerator.WriteSql(allSchemaNames, writer);

            StoreOptions.AllDocumentMappings.Each(x => x.SchemaObjects.WriteSchemaObjects(this, writer));

            if (Events.IsActive && !StoreOptions.AllDocumentMappings.Contains(Events.As<IDocumentMapping>()))
            {
                Events.As<IDocumentMapping>().SchemaObjects.WriteSchemaObjects(this, writer);
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

        public IdAssignment<T> IdAssignmentFor<T>()
        {
            return _identityAssignments.GetOrAdd(typeof(T), t =>
            {
                var mapping = MappingFor(typeof(T));
                return mapping.ToIdAssignment<T>(this);


            }).As<IdAssignment<T>>();
        }


        public IQueryHandlerFactory HandlerFactory { get; }

        public void ResetSchemaExistenceChecks()
        {
            AllDocumentMaps().Each(x => x.SchemaObjects.ResetSchemaExistenceChecks());
            Events.As<EventGraph>().SchemaObjects.ResetSchemaExistenceChecks();

            _documentTypes.Clear();
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
                x => x.SchemaObjects.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, executeSql));
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