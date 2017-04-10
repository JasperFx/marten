using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Transforms;
using Marten.Util;

namespace Marten.Schema
{
    public class DocumentSchema : IDocumentSchema, IDDLRunner, IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> _bulkLoaders = new ConcurrentDictionary<Type, object>();

        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes =
            new ConcurrentDictionary<Type, IDocumentStorage>();

        private readonly Lazy<EventQueryMapping> _eventQuery;

        private readonly IConnectionFactory _factory;

        private readonly ConcurrentDictionary<Type, object> _identityAssignments =
            new ConcurrentDictionary<Type, object>();

        private readonly IMartenLogger _logger;

        private readonly Lazy<SequenceFactory> _sequences;

        private readonly IDictionary<string, SystemFunction> _systemFunctions = new Dictionary<string, SystemFunction>();

        private readonly ConcurrentDictionary<string, TransformFunction> _transforms =
            new ConcurrentDictionary<string, TransformFunction>();

        private readonly ConcurrentDictionary<Type, IDocumentUpsert> _upserts =
            new ConcurrentDictionary<Type, IDocumentUpsert>();

        public DocumentSchema(DocumentStore store, IConnectionFactory factory, IMartenLogger logger)
        {
            _factory = factory;
            _logger = logger;

            StoreOptions = store.Options;

            _sequences = new Lazy<SequenceFactory>(() =>
            {
                var sequences = new SequenceFactory(this, _factory, StoreOptions, _logger);

                var patch = new SchemaPatch(StoreOptions.DdlRules);

                sequences.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);

                apply(sequences, patch);

                return sequences;
            });

            DbObjects = new DbObjects(_factory, this);


            addSystemFunction(StoreOptions, "mt_immutable_timestamp", "text");

            _eventQuery = new Lazy<EventQueryMapping>(() => new EventQueryMapping(StoreOptions));
        }


        public IEnumerable<SystemFunction> SystemFunctions => _systemFunctions.Values;


        void IDDLRunner.Apply(object subject, string ddl)
        {
            if (ddl.Trim().IsEmpty()) return;

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


        public void Dispose()
        {
        }

        public void EnsureFunctionExists(string functionName)
        {
            var systemFunction = _systemFunctions[functionName];

            if (!systemFunction.Checked)
                systemFunction.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this,
                    new SchemaPatch(this));
        }

        public IDbObjects DbObjects { get; }
        public IEnumerable<IDocumentMapping> AllMappings => StoreOptions.AllDocumentMappings;

        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            EnsureStorageExists(typeof(T));

            return _bulkLoaders.GetOrAdd(typeof(T), t =>
            {
                var assignment = IdAssignmentFor<T>();

                var mapping = MappingFor(typeof(T));

                if (mapping is DocumentMapping)
                    return new BulkLoader<T>(StoreOptions.Serializer(), mapping.As<DocumentMapping>(), assignment,
                        StoreOptions.UseCharBufferPooling);


                throw new ArgumentOutOfRangeException("T", "Marten cannot do bulk inserts of " + typeof(T).FullName);
            }).As<IBulkLoader<T>>();
        }

        public IDocumentUpsert UpsertFor(Type documentType)
        {
            EnsureStorageExists(documentType);

            return _upserts.GetOrAdd(documentType, type => { return MappingFor(documentType).BuildUpsert(this); });
        }


        public StoreOptions StoreOptions { get; }

        public IDocumentMapping MappingFor(Type documentType)
        {
            if (documentType == typeof(IEvent))
                return _eventQuery.Value;

            return StoreOptions.FindMapping(documentType);
        }

        public void EnsureStorageExists(Type documentType)
        {
            // TODO -- HACK! Do something later that's more systematic
            if (documentType == typeof(StreamState)) return;

            if (documentType == typeof(EventStream))
            {
                var patch = new SchemaPatch(this);
                Events.SchemaObjects
                    .GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);

                return;
            }

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

                var storage = mapping.BuildStorage(this);

                buildSchemaObjectsIfNecessary(mapping);

                return storage;
            });
        }

        public EventGraph Events => StoreOptions.Events;


        public string[] AllSchemaNames()
        {
            var schemas =
                AllMappings.OfType<DocumentMapping>().Select(x => x.DatabaseSchemaName).Distinct().ToList();

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

        public void WritePatch(string filename, bool withSchemas = true)
        {
            if (!Path.IsPathRooted(filename))
                filename = AppContext.BaseDirectory.AppendPath(filename);

            var patch = ToPatch(withSchemas);

            patch.WriteUpdateFile(filename);

            var dropFile = SchemaPatch.ToDropFileName(filename);
            patch.WriteRollbackFile(dropFile);
        }

        public SchemaPatch ToPatch(bool withSchemas = true)
        {
            var patch = new SchemaPatch(StoreOptions.DdlRules);

            if (withSchemas)
            {
                var allSchemaNames = AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, patch.UpWriter);
            }

            foreach (var schemaObject in AllSchemaObjects())
                schemaObject.WritePatch(this, patch);

            return patch;
        }

        public SchemaPatch ToPatch(Type documentType)
        {
            var mapping = MappingFor(documentType);
            var patch = new SchemaPatch(StoreOptions.DdlRules);
            mapping.SchemaObjects.WritePatch(this, patch);

            return patch;
        }

        public void AssertDatabaseMatchesConfiguration()
        {
            var patch = ToPatch(false);

            if (patch.UpdateDDL.Trim().IsNotEmpty())
                throw new SchemaValidationException(patch.UpdateDDL);
        }

        public void ApplyAllConfiguredChangesToDatabase()
        {
            var patch = new SchemaPatch(this);

            var allSchemaNames = AllSchemaNames();
            DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, patch.UpWriter);

            patch.Updates.Apply(this, patch.UpdateDDL);

            foreach (var schemaObject in AllSchemaObjects())
                schemaObject.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);
        }

        public void WriteDDLByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var schemaObjects = AllSchemaObjects().ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, schemaObjects);


            foreach (var schemaObject in schemaObjects)
            {
                var writer = new StringWriter();
                schemaObject.WriteSchemaObjects(this, writer);

                var file = directory.AppendPath(schemaObject.Name + ".sql");

                new SchemaPatch(StoreOptions.DdlRules).WriteTransactionalFile(file, writer.ToString());
            }
        }


        public void WritePatchByType(string directory)
        {
            var system = new FileSystem();

            system.DeleteDirectory(directory);
            system.CreateDirectory(directory);

            var schemaObjects = AllSchemaObjects().ToArray();
            writeDatabaseSchemaGenerationScript(directory, system, schemaObjects);


            foreach (var schemaObject in schemaObjects)
            {
                var patch = new SchemaPatch(StoreOptions.DdlRules);
                schemaObject.WritePatch(this, patch);

                if (patch.UpdateDDL.IsNotEmpty())
                {
                    var file = directory.AppendPath(schemaObject.Name + ".sql");
                    patch.WriteUpdateFile(file);
                }
            }
        }

        public string ToDDL()
        {
            var writer = new StringWriter();

            new SchemaPatch(StoreOptions.DdlRules).WriteTransactionalScript(writer, w =>
            {
                var allSchemaNames = AllSchemaNames();
                DatabaseSchemaGenerator.WriteSql(StoreOptions, allSchemaNames, w);

                foreach (var schemaObject in AllSchemaObjects())
                    schemaObject.WriteSchemaObjects(this, writer);
            });


            return writer.ToString();
        }


        public IDocumentStorage<T> StorageFor<T>()
        {
            return StorageFor(typeof(T)).As<IDocumentStorage<T>>();
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

                var patch = new SchemaPatch(StoreOptions.DdlRules);

                transform.GenerateSchemaObjectsIfNecessary(StoreOptions.AutoCreateSchemaObjects, this, patch);

                apply(transform, patch);

                return transform;
            });
        }

        public void ResetSchemaExistenceChecks()
        {
            if (_sequences.IsValueCreated)
                _sequences.Value.ResetSchemaExistenceChecks();

            foreach (var schemaObject in AllSchemaObjects())
                schemaObject.ResetSchemaExistenceChecks();

            _documentTypes.Clear();
            _transforms.Clear();
        }

        private void addSystemFunction(StoreOptions options, string functionName, string args)
        {
            _systemFunctions.Add(functionName, new SystemFunction(options, functionName, args));
        }

        public IEnumerable<ISchemaObjects> AllSchemaObjects()
        {
            var mappings = AllMappings.OrderBy(x => x.DocumentType.Name).TopologicalSort(m =>
            {
                var documentMapping = m as DocumentMapping;
                if (documentMapping == null)
                    return Enumerable.Empty<IDocumentMapping>();

                return documentMapping.ForeignKeys
                    .Where(x => x.ReferenceDocumentType != documentMapping.DocumentType)
                    .Select(keyDefinition => keyDefinition.ReferenceDocumentType)
                    .Select(MappingFor);
            });

            foreach (var function in _systemFunctions.Values.OrderBy(x => x.Name))
                yield return function;

            foreach (var mapping in mappings)
                yield return mapping.SchemaObjects;

            yield return new SequenceFactory(this, _factory, StoreOptions, _logger);

            foreach (var transform in StoreOptions.Transforms.AllFunctions().OrderBy(x => x.Name))
                yield return transform;

            if (Events.IsActive)
                yield return Events.SchemaObjects;
        }


        private void writeDatabaseSchemaGenerationScript(string directory, FileSystem system,
            ISchemaObjects[] schemaObjects)
        {
            var allSchemaNames = AllSchemaNames();
            var script = DatabaseSchemaGenerator.GenerateScript(StoreOptions, allSchemaNames);

            var writer = new StringWriter();

            if (script.IsNotEmpty())
            {
                writer.WriteLine(script);

                writer.WriteLine();
            }

            foreach (var schemaObject in schemaObjects)
                writer.WriteLine($"\\i {schemaObject.Name}.sql");

            var filename = directory.AppendPath("all.sql");
            system.WriteStringToFile(filename, writer.ToString());
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
                    return Enumerable.Empty<IDocumentMapping>();

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
            var duplicates =
                StoreOptions.AllDocumentMappings.Where(x => !x.StructuralTyped)
                    .GroupBy(x => x.Alias)
                    .Where(x => x.Count() > 1)
                    .ToArray();
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

#if SERIALIZE
    [Serializable]
#endif

    public class AmbiguousDocumentTypeAliasesException : Exception
    {
        public AmbiguousDocumentTypeAliasesException(string message) : base(message)
        {
        }

#if SERIALIZE
        protected AmbiguousDocumentTypeAliasesException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }


#if SERIALIZE
    [Serializable]
#endif

    public class SchemaValidationException : Exception
    {
        public SchemaValidationException(string ddl)
            : base("Configuration to Schema Validation Failed! These changes detected:\n\n" + ddl)
        {
        }

#if SERIALIZE
        protected SchemaValidationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}