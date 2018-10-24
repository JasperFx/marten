using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Transforms;
using Npgsql;

namespace Marten.Storage
{
    public class DefaultTenancy : Tenancy, ITenancy
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

    public class LightweightTenant : ITenant
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

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _inner.StorageFor(documentType);
        }

        public IDocumentMapping MappingFor(Type documentType)
        {
            return _inner.MappingFor(documentType);
        }

        public void EnsureStorageExists(Type documentType)
        {
            _inner.EnsureStorageExists(documentType);
        }

        public ISequences Sequences => _inner.Sequences;
        public IDocumentStorage<T> StorageFor<T>()
        {
            return _inner.StorageFor<T>();
        }

        public IdAssignment<T> IdAssignmentFor<T>()
        {
            return _inner.IdAssignmentFor<T>();
        }

        public TransformFunction TransformFor(string name)
        {
            return _inner.TransformFor(name);
        }

        public void ResetSchemaExistenceChecks()
        {
            _inner.ResetSchemaExistenceChecks();
        }

        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            return _inner.BulkLoaderFor<T>();
        }

        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int timeout = 30)
        {
            return _inner.OpenConnection(mode, isolationLevel, timeout);
        }

        public void ResetHiloSequenceFloor<T>(long floor)
        {
            _inner.ResetHiloSequenceFloor<T>(floor);
        }

        public DocumentMetadata MetadataFor<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, StorageFor(typeof(T)),
                MappingFor(typeof(T)).As<DocumentMapping>());

            using (var connection = OpenConnection())
            {
                return connection.Fetch(handler, null, null, this);
            }
        }

        public async Task<DocumentMetadata> MetadataForAsync<T>(T entity, CancellationToken token = new CancellationToken())
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, StorageFor(typeof(T)),
                MappingFor(typeof(T)).As<DocumentMapping>());

            using (var connection = OpenConnection())
            {
                return await connection.FetchAsync(handler, null, null, this, token).ConfigureAwait(false);
            }
        }

        public NpgsqlConnection CreateConnection()
        {
            return _inner.CreateConnection();
        }
    }
}