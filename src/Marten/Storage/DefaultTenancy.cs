using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Transforms;
using Npgsql;

namespace Marten.Storage
{
    public class DefaultTenancy: Tenancy, ITenancy
    {
        public DefaultTenancy(IConnectionFactory factory, StoreOptions options) : base(options)
        {
            Default = new Tenant(options.Storage, options, factory, DefaultTenantId);
            Cleaner = new DocumentCleaner(options, Default);
            Schema = new TenantSchema(options, Default.As<Tenant>());
        }

        public ITenant this[string tenantId] => new LightweightTenant(tenantId, Default, Options.RetryPolicy());

        public ITenant Default { get; }

        public void Initialize()
        {
            seedSchemas(Default);
        }

        public IDocumentCleaner Cleaner { get; }
        public IDocumentSchema Schema { get; }
        public TenancyStyle Style { get; } = TenancyStyle.Conjoined;
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

        public IDbObjects DbObjects => _inner.DbObjects;

        public string TenantId { get; }

        public IDocumentStorage<T> StorageFor<T>()
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

        public ISequences Sequences => _inner.Sequences;

        public TransformFunction TransformFor(string name)
        {
            return _inner.TransformFor(name);
        }

        public void ResetSchemaExistenceChecks()
        {
            _inner.ResetSchemaExistenceChecks();
        }

        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int? timeout = null)
        {
            return _inner.OpenConnection(mode, isolationLevel, timeout);
        }

        public void ResetHiloSequenceFloor<T>(long floor)
        {
            _inner.ResetHiloSequenceFloor<T>(floor);
        }

        public NpgsqlConnection CreateConnection()
        {
            return _inner.CreateConnection();
        }

        public IProviderGraph Providers => _inner.Providers;
    }
}
