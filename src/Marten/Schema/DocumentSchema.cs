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
using Marten.Transforms;
using Marten.Util;

namespace Marten.Schema
{

    public class DocumentSchema : IDocumentSchema, IDDLRunner, IDisposable
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
        private readonly ConcurrentDictionary<string, TransformFunction> _transforms = new ConcurrentDictionary<string, TransformFunction>();

        private readonly Lazy<SequenceFactory> _sequences;

        private readonly IDictionary<string, SystemFunction> _systemFunctions = new Dictionary<string, SystemFunction>();

        public DocumentSchema(StoreOptions options, IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;

            StoreOptions = options;

            options.AllDocumentMappings.Each(x => _mappings[x.DocumentType] = x);

            _sequences = new Lazy<SequenceFactory>(() =>
            {
                var sequences = new SequenceFactory(this, _factory, options, _logger);
                
                var patch = new SchemaPatch();

                sequences.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);

                apply(sequences, patch);

                return sequences;
            });

            Parser = new MartenExpressionParser(StoreOptions.Serializer(), StoreOptions);

            HandlerFactory = new QueryHandlerFactory(this, options.Serializer());

            DbObjects = new DbObjects(_factory, this);


            addSystemFunction(options, "mt_immutable_timestamp");
        }

        private void addSystemFunction(StoreOptions options, string functionName)
        {
            _systemFunctions.Add(functionName, new SystemFunction(options, functionName));
        }



        public void Dispose()
        {
        }

        public void EnsureFunctionExists(string functionName)
        {
            var systemFunction = _systemFunctions[functionName];

            if (!systemFunction.Checked)
            {
                systemFunction.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, new SchemaPatch(this));
            }
        }

        public IEnumerable<ISchemaObjects> AllSchemaObjects()
        {
            var mappings = AllDocumentMaps().TopologicalSort(m =>
            {
                var documentMapping = m as DocumentMapping;
                if (documentMapping == null)
                {
                    return Enumerable.Empty<IDocumentMapping>();
                }

                return documentMapping.ForeignKeys
                    .Select(keyDefinition => keyDefinition.ReferenceDocumentType)
                    .Select(MappingFor);
            });

            foreach (var function in _systemFunctions.Values)
            {
                yield return function;
            }

            foreach (var mapping in mappings)
            {
                yield return mapping.SchemaObjects;
            }

            yield return new SequenceFactory(this, _factory, StoreOptions, _logger);

            foreach (var transform in StoreOptions.Transforms.AllFunctions())
            {
                yield return transform;
            }

            if (Events.IsActive)
            {
                yield return Events.As<EventGraph>().SchemaObjects;
            }
        }

        public IDbObjects DbObjects { get; }
        public IEnumerable<IDocumentMapping> AllMappings => _mappings.Values;

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

        public ISequences Sequences => _sequences.Value;

        public void WriteDDL(string filename)
        {
            var sql = ToDDL();
            new FileSystem().WriteStringToFile(filename, sql);
        }

        public void WritePatch(string filename)
        {
            var patch = ToPatch();
            var fileSystem = new FileSystem();


            fileSystem.WriteStringToFile(filename, patch.UpdateDDL);

            var dropFile = SchemaPatch.ToDropFileName(filename);
            fileSystem.WriteStringToFile(dropFile, patch.RollbackDDL);

        }

        public SchemaPatch ToPatch(bool withSchemas = true)
        {
            var patch = new SchemaPatch();

            if (withSchemas)
            {
                var allSchemaNames = AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(allSchemaNames, patch.UpWriter);
            }

            foreach (var schemaObject in AllSchemaObjects())
            {
                schemaObject.WritePatch(this, patch);
            }

            return patch;
        }
        
        public void AssertDatabaseMatchesConfiguration()
        {
            var patch = ToPatch(false);

            if (patch.UpdateDDL.Trim().IsNotEmpty())
            {
                throw new SchemaValidationException(patch.UpdateDDL);
            }
        }

        public void ApplyAllConfiguredChangesToDatabase()
        {
            var patch = new SchemaPatch(this);

            var allSchemaNames = AllSchemaNames();
            DatabaseSchemaGenerator.WriteSql(allSchemaNames, patch.UpWriter);

            patch.Updates.Apply(this, patch.UpdateDDL);

            foreach (var schemaObject in AllSchemaObjects())
            {
                schemaObject.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);
            }
        }

        public void WriteDDLByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var schemaObjects = AllSchemaObjects().ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, schemaObjects);

            var fileSystem = new FileSystem();

            foreach (var schemaObject in schemaObjects)
            {
                var writer = new StringWriter();
                schemaObject.WriteSchemaObjects(this, writer);

                var file = directory.AppendPath(schemaObject.Name + ".sql");
                fileSystem.WriteStringToFile(file, writer.ToString());
            }
        }


        public string ToDDL()
        {
            var writer = new StringWriter();

            var allSchemaNames = AllSchemaNames();
            DatabaseSchemaGenerator.WriteSql(allSchemaNames, writer);

            foreach (var schemaObject in AllSchemaObjects())
            {
                schemaObject.WriteSchemaObjects(this, writer);
            }

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

        public TransformFunction TransformFor(string name)
        {
            return _transforms.GetOrAdd(name, key =>
            {
                var transform = StoreOptions.Transforms.For(key);

                var patch = new SchemaPatch();

                transform.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);

                apply(transform, patch);

                return transform;
            });
        }


        public IQueryHandlerFactory HandlerFactory { get; }
        public IEnumerable<SystemFunction> SystemFunctions => _systemFunctions.Values;

        public void ResetSchemaExistenceChecks()
        {
            if (_sequences.IsValueCreated)
            {
                _sequences.Value.ResetSchemaExistenceChecks();
            }

            foreach (var schemaObject in AllSchemaObjects())
            {
                schemaObject.ResetSchemaExistenceChecks();
            }

            _documentTypes.Clear();
            _transforms.Clear();
        }


        private void writeDatabaseSchemaGenerationScript(string directory, FileSystem system, ISchemaObjects[] schemaObjects)
        {
            var allSchemaNames = AllSchemaNames();
            var script = DatabaseSchemaGenerator.GenerateScript(allSchemaNames);

            var writer = new StringWriter();

            if (script.IsNotEmpty())
            {
                writer.WriteLine(script);

                writer.WriteLine();
            }

            foreach (var schemaObject in schemaObjects)
            {
                writer.WriteLine($"\\i {schemaObject.Name}.sql");
            }

            var filename = directory.AppendPath("all.sql");
            system.WriteStringToFile(filename, writer.ToString());
        }


        void IDDLRunner.Apply(object subject, string ddl)
        {
            try
            {
                _factory.RunSql(ddl);
                _logger.SchemaChange(ddl);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(subject, ddl, e);
            }
        }

        private void apply(object subject, SchemaPatch patch)
        {
            var ddl = patch.UpdateDDL.Trim();
            if (ddl.IsEmpty()) return;

            try
            {
                _factory.RunSql(ddl);
                _logger.SchemaChange(ddl);
            }
            catch (Exception e)
            {
                throw new MartenSchemaException(subject, ddl, e);
            }
        }


        private void buildSchemaObjectsIfNecessary(IDocumentMapping mapping)
        {

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

            var patch = new SchemaPatch(this);

            sortedMappings.Each(
                x => x.SchemaObjects.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch));
            
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

        public void RebuildSystemFunctions()
        {
            _systemFunctions.Values.Each(
                x =>
                    x.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, new SchemaPatch(this)));
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
    

    [Serializable]
    public class SchemaValidationException : Exception
    {
        public SchemaValidationException(string ddl) : base("Configuration to Schema Validation Failed! These changes detected:\n\n" + ddl)
        {
        }

        protected SchemaValidationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}