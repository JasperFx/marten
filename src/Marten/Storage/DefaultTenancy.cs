using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;

#nullable enable
namespace Marten.Storage
{
    internal class DefaultTenancy: Tenancy, ITenancy
    {
        public DefaultTenancy(IConnectionFactory factory, StoreOptions options): base(options)
        {
            Default = new Tenant(options.Storage, options, factory, DefaultTenantId);
            Cleaner = new DocumentCleaner(options, Default);
            Schema = new TenantSchema(options, Default.As<Tenant>());
        }

        public TenancyStyle Style { get; } = TenancyStyle.Conjoined;

        public ITenant this[string tenantId] => new LightweightTenant(tenantId, Default, Options.RetryPolicy());

        public ITenant Default { get; }

        public void Initialize()
        {
            seedSchemas(Default);
        }

        public IDocumentCleaner Cleaner { get; }
        public IDocumentSchema Schema { get; }
    }

    public class LightweightTenant: ITenant
    {
        private readonly ITenant _inner;
        private readonly IRetryPolicy _retryPolicy;

        public LightweightTenant(string tenantId, ITenant inner, IRetryPolicy retryPolicy)
        {
            _inner = inner;
            TenantId = tenantId;
            _retryPolicy = retryPolicy;
        }

        public string TenantId { get; }

        public IDocumentStorage<T> StorageFor<T>() where T : notnull
        {
            return _inner.StorageFor<T>();
        }

        void ITenantStorage.MarkAllFeaturesAsChecked()
        {
            _inner.MarkAllFeaturesAsChecked();
        }

        public void EnsureStorageExists(Type documentType)
        {
            _inner.EnsureStorageExists(documentType);
        }

        public Task EnsureStorageExistsAsync(Type featureType, CancellationToken token = default)
        {
            return _inner.EnsureStorageExistsAsync(featureType, token);
        }

        public IFeatureSchema FindFeature(Type storageType)
        {
            return _inner.FindFeature(storageType);
        }

        public ISequences Sequences => _inner.Sequences;

        public void ResetSchemaExistenceChecks()
        {
            _inner.ResetSchemaExistenceChecks();
        }

        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int? timeout = null)
        {
            return _inner.OpenConnection(mode, isolationLevel, timeout);
        }

        public Task ResetHiloSequenceFloor<T>(long floor)
        {
            return _inner.ResetHiloSequenceFloor<T>(floor);
        }

        public NpgsqlConnection CreateConnection()
        {
            return _inner.CreateConnection();
        }

        public IProviderGraph Providers => _inner.Providers;

        public Task<IReadOnlyList<DbObjectName>> SchemaTables()
        {
            return _inner.SchemaTables();
        }

        public Task<IReadOnlyList<DbObjectName>> DocumentTables()
        {
            return _inner.DocumentTables();
        }

        public Task<IReadOnlyList<DbObjectName>> Functions()
        {
            return _inner.Functions();
        }

        public Task<Function> DefinitionForFunction(DbObjectName function)
        {
            return _inner.DefinitionForFunction(function);
        }

        public Task<Table> ExistingTableFor(Type type)
        {
            return _inner.ExistingTableFor(type);
        }
    }
}
