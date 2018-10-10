using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Transforms;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public class Tenant : ITenant
    {
        private readonly ConcurrentDictionary<Type, bool> _checks = new ConcurrentDictionary<Type, bool>();
        private readonly IConnectionFactory _factory;
        private readonly StorageFeatures _features;
        private readonly StoreOptions _options;
        private Lazy<SequenceFactory> _sequences;

        public Tenant(StorageFeatures features, StoreOptions options, IConnectionFactory factory, string tenantId)
        {
            TenantId = tenantId;
            _features = features;
            _options = options;
            _factory = factory;

            resetSequences();

        }

        private void resetSequences()
        {
            _sequences = new Lazy<SequenceFactory>(() =>
            {
                var sequences = new SequenceFactory(_options, this);

                generateOrUpdateFeature(typeof(SequenceFactory), sequences);


                return sequences;
            });
        }

        public string TenantId { get; }
        public IDbObjects DbObjects => new DbObjects(this, _features);

        public void RemoveSchemaItems(Type featureType, StorageFeatures features)
        {
            var feature = features.FindFeature(featureType);
            var writer = new StringWriter();

            foreach (var schemaObject in feature.Objects)
            {
                schemaObject.WriteDropStatement(_options.DdlRules, writer);
            }

            _factory.RunSql(writer.ToString());
        }

        public void ResetSchemaExistenceChecks()
        {
            _checks.Clear();
            resetSequences();
        }

        public void EnsureStorageExists(Type featureType)
        {
            if (_options.AutoCreateSchemaObjects == AutoCreate.None) return;

            ensureStorageExists(new List<Type>(), featureType);
        }

        private void ensureStorageExists(IList<Type> types, Type featureType)
        {
            if (_checks.ContainsKey(featureType)) return;


            // TODO -- ensure the system type here too?
            var feature = _features.FindFeature(featureType);

            feature.AssertValidNames(_options);

            if (feature == null)
                throw new ArgumentOutOfRangeException(nameof(featureType),
                    $"Unknown feature type {featureType.FullName}");

            if (_checks.ContainsKey(feature.StorageType))
            {
                _checks[featureType] = true;
                return;
            }

            // Preventing cyclic dependency problems
            if (types.Contains(featureType)) return;

            types.Fill(featureType);

            foreach (var dependentType in feature.DependentTypes())
            {
                ensureStorageExists(types, dependentType);
            }

            // TODO -- might need to do a lock here.
            generateOrUpdateFeature(featureType, feature);

        }

        
        private readonly object _updateLock = new object();

        private void generateOrUpdateFeature(Type featureType, IFeatureSchema feature)
        {
            lock (_updateLock)
            {
                using (var conn = _factory.Create())
                {
                    conn.Open();

                    var patch = new SchemaPatch(_options.DdlRules);
                    patch.Apply(conn, _options.AutoCreateSchemaObjects, feature.Objects);
                    patch.AssertPatchingIsValid(_options.AutoCreateSchemaObjects);

                    var ddl = patch.UpdateDDL;
                    if (patch.Difference != SchemaPatchDifference.None && ddl.IsNotEmpty())
                    {
                        var cmd = conn.CreateCommand(ddl);
                        try
                        {
                            cmd.ExecuteNonQuery();
                            _options.Logger().SchemaChange(ddl);
                            RegisterCheck(featureType, feature);
                        }
                        catch (Exception e)
                        {
                            throw new MartenCommandException(cmd, e);
                        }
                    }
                    else if (patch.Difference == SchemaPatchDifference.None)
                    {
                        RegisterCheck(featureType, feature);
                    }
                }
            }
        }

        private void RegisterCheck(Type featureType, IFeatureSchema feature)
        {
            _checks[featureType] = true;
            if (feature.StorageType != featureType)
            {
                _checks[feature.StorageType] = true;
            }
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            EnsureStorageExists(documentType);
            return _features.StorageFor(documentType);
        }

        public IDocumentMapping MappingFor(Type documentType)
        {
            EnsureStorageExists(documentType);
            return _features.FindMapping(documentType);
        }

        public ISequences Sequences => _sequences.Value;

        public IDocumentStorage<T> StorageFor<T>()
        {
            EnsureStorageExists(typeof(T));
            return _features.StorageFor(typeof(T)).As<IDocumentStorage<T>>();
        }

        private readonly ConcurrentDictionary<Type, object> _identityAssignments =
             new ConcurrentDictionary<Type, object>();

        public IdAssignment<T> IdAssignmentFor<T>()
        {
            EnsureStorageExists(typeof(T));
            return _identityAssignments.GetOrAdd(typeof(T), t =>
            {
                var mapping = MappingFor(typeof(T));
                return mapping.ToIdAssignment<T>(this);
            }).As<IdAssignment<T>>();
        }

        public TransformFunction TransformFor(string name)
        {
            EnsureStorageExists(typeof(Transforms.Transforms));
            return _features.Transforms.For(name);
        }

        private readonly ConcurrentDictionary<Type, object> _bulkLoaders = new ConcurrentDictionary<Type, object>();


        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            EnsureStorageExists(typeof(T));
            return _bulkLoaders.GetOrAdd(typeof(T), t =>
            {
                var assignment = IdAssignmentFor<T>();

                var mapping = MappingFor(typeof(T)).Root as DocumentMapping;

                if (mapping == null) throw new ArgumentOutOfRangeException("Marten cannot do bulk inserts on documents of type " + typeof(T).FullName);

                return new BulkLoader<T>(_options.Serializer(), mapping, assignment);
            }).As<IBulkLoader<T>>();
        }


        public void MarkAllFeaturesAsChecked()
        {
            foreach (var feature in _features.AllActiveFeatures(this))
            {
                _checks[feature.StorageType] = true;
            }
        }

        /// <summary>
        ///     Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int timeout = 30)
        {
            return new ManagedConnection(_factory, mode, isolationLevel, timeout);
        }

        /// <summary>
        /// Fetch a connection to the tenant database
        /// </summary>
        /// <returns></returns>
        public NpgsqlConnection CreateConnection()
        {
            return _factory.Create();
        }


        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public void ResetHiloSequenceFloor<T>(long floor)
        {
            // Make sure that the sequence is built for this one
            IdAssignmentFor<T>();
            var sequence = Sequences.SequenceFor(typeof(T));
            sequence.SetFloor(floor);
        }

        /// <summary>
        ///     Fetch the entity version and last modified time from the database
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public DocumentMetadata MetadataFor<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var mapping = MappingFor(typeof(T));
            var handler = new EntityMetadataQueryHandler(entity, StorageFor(typeof(T)),
                mapping);

            using (var connection = OpenConnection())
            {
                return connection.Fetch(handler, null, null, this);
            }
        }

        /// <summary>
        ///     Fetch the entity version and last modified time from the database
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<DocumentMetadata> MetadataForAsync<T>(T entity,
            CancellationToken token = default(CancellationToken))
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, StorageFor(typeof(T)),
                MappingFor(typeof(T)));

            using (var connection = OpenConnection())
            {
                return await connection.FetchAsync(handler, null, null, this, token).ConfigureAwait(false);
            }
        }
    }
}