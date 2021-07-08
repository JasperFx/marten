using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;

namespace Marten.Storage
{
    internal class Tenant: ITenant
    {
        private readonly ConcurrentDictionary<Type, bool> _checks = new();
        private readonly IConnectionFactory _factory;
        private readonly StorageFeatures _features;

        private readonly ConcurrentDictionary<Type, object> _identityAssignments =
            new();

        private readonly StoreOptions _options;

        private Lazy<SequenceFactory> _sequences;

        public Tenant(StorageFeatures features, StoreOptions options, IConnectionFactory factory, string tenantId)
        {
            TenantId = tenantId;
            _features = features;
            _options = options;
            _factory = factory;

            resetSequences();

            Providers = options.AutoCreateSchemaObjects == AutoCreate.None
                ? options.Providers
                : new StorageCheckingProviderGraph(this, options.Providers);
        }

        public string TenantId { get; }

        public IFeatureSchema FindFeature(Type storageType)
        {
            return _options.Storage.FindFeature(storageType);
        }


        public void ResetSchemaExistenceChecks()
        {
            _checks.Clear();
            resetSequences();
            if (Providers is StorageCheckingProviderGraph)
            {
                Providers = new StorageCheckingProviderGraph(this, _options.Providers);
            }
        }

        public void EnsureStorageExists(Type featureType)
        {
            if (_options.AutoCreateSchemaObjects == AutoCreate.None)
            {
                return;
            }

            ensureStorageExists(new List<Type>(), featureType).GetAwaiter().GetResult();
        }

        public Task EnsureStorageExistsAsync(Type featureType, CancellationToken token)
        {
            return _options.AutoCreateSchemaObjects == AutoCreate.None
                ? Task.CompletedTask
                : ensureStorageExists(new List<Type>(), featureType, token);
        }


        public IDocumentStorage<T> StorageFor<T>()
        {
            return Providers.StorageFor<T>().QueryOnly;
        }

        public ISequences Sequences => _sequences.Value;

        public void MarkAllFeaturesAsChecked()
        {
            foreach (var feature in _features.AllActiveFeatures(this)) _checks[feature.StorageType] = true;
        }

        /// <summary>
        ///     Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int? timeout = null)
        {
            return new ManagedConnection(_factory, mode, _options.RetryPolicy(), isolationLevel, timeout);
        }

        /// <summary>
        ///     Fetch a connection to the tenant database
        /// </summary>
        /// <returns></returns>
        public NpgsqlConnection CreateConnection()
        {
            return _factory.Create();
        }

        public IProviderGraph Providers { get; private set; }

        /// <summary>
        ///     Set the minimum sequence number for a Hilo sequence for a specific document type
        ///     to the specified floor. Useful for migrating data between databases
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public Task ResetHiloSequenceFloor<T>(long floor)
        {
            var sequence = Sequences.SequenceFor(typeof(T));
            return sequence.SetFloor(floor);
        }

        private void resetSequences()
        {
            _sequences = new Lazy<SequenceFactory>(() =>
            {
                var sequences = new SequenceFactory(_options, this);

                generateOrUpdateFeature(typeof(SequenceFactory), sequences, default).GetAwaiter().GetResult();

                return sequences;
            });
        }

        private async Task ensureStorageExists(IList<Type> types, Type featureType, CancellationToken token = default)
        {
            if (_checks.ContainsKey(featureType))
            {
                return;
            }

            var feature = _features.FindFeature(featureType);

            if (feature == null)
            {
                throw new ArgumentOutOfRangeException(nameof(featureType),
                    $"Unknown feature type {featureType.FullName}");
            }

            if (_checks.ContainsKey(feature.StorageType))
            {
                _checks[featureType] = true;
                return;
            }

            // Preventing cyclic dependency problems
            if (types.Contains(featureType))
            {
                return;
            }

            types.Fill(featureType);

            foreach (var dependentType in feature.DependentTypes())
            {
                await ensureStorageExists(types, dependentType, token);
            }

            await generateOrUpdateFeature(featureType, feature, token);
        }


        private readonly TimedLock _migrateLocker = new TimedLock();

        private async Task generateOrUpdateFeature(Type featureType, IFeatureSchema feature, CancellationToken token)
        {
            if (_checks.ContainsKey(featureType))
            {
                RegisterCheck(featureType, feature);
                return;
            }

            var schemaObjects = feature.Objects;
            schemaObjects.AssertValidNames(_options);

            using (await _migrateLocker.Lock(5.Seconds()))
            {
                if (_checks.ContainsKey(featureType))
                {
                    RegisterCheck(featureType, feature);
                    return;
                }

                await executeMigration(schemaObjects, token);
                RegisterCheck(featureType, feature);
            }
        }

        private async Task executeMigration(ISchemaObject[] schemaObjects, CancellationToken token = default)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync(token);

            var migration = await SchemaMigration.Determine(conn, schemaObjects);

            if (migration.Difference == SchemaPatchDifference.None) return;

            migration.AssertPatchingIsValid(_options.AutoCreateSchemaObjects);

            await migration.ApplyAll(
                conn,
                _options.Advanced.DdlRules,
                _options.AutoCreateSchemaObjects,
                sql => _options.Logger().SchemaChange(sql),
                MartenExceptionTransformer.WrapAndThrow);

        }

        private void RegisterCheck(Type featureType, IFeatureSchema feature)
        {
            _checks[featureType] = true;
            if (feature.StorageType != featureType)
            {
                _checks[feature.StorageType] = true;
            }
        }


        public async Task<IReadOnlyList<DbObjectName>> SchemaTables()
        {
            var schemaNames = _features.AllSchemaNames();

            using var conn = CreateConnection();
            await conn.OpenAsync();

            return await conn.ExistingTables(schemas:schemaNames);
        }

        public async Task<IReadOnlyList<DbObjectName>> DocumentTables()
        {
            var tables = await SchemaTables();
            return tables.Where(x => x.Name.StartsWith(SchemaConstants.TablePrefix)).ToList();
        }

        public async Task<IReadOnlyList<DbObjectName>> Functions()
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var schemaNames = _features.AllSchemaNames();
            return await conn.ExistingFunctions(namePattern:"mt_%", schemas:schemaNames);
        }

        public async Task<Function> DefinitionForFunction(DbObjectName function)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            return await conn.FindExistingFunction(function);
        }

        public async Task<Table> ExistingTableFor(Type type)
        {
            var mapping = _features.MappingFor(type).As<DocumentMapping>();
            var expected = mapping.Schema.Table;

            using var conn = CreateConnection();
            await conn.OpenAsync();

            return await expected.FetchExisting(conn);
        }
    }
}
