using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten
{
    public class AdvancedOptions
    {
        private readonly ISerializer _serializer;
        private readonly IDocumentSchema _schema;

        /// <summary>
        /// The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        public StoreOptions Options { get; }

        public AdvancedOptions(IDocumentCleaner cleaner, StoreOptions options, ISerializer serializer, IDocumentSchema schema)
        {
            _serializer = serializer;
            _schema = schema;
            Options = options;
            Clean = cleaner;
        }

        /// <summary>
        /// Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean { get; }


        public ISerializer Serializer => _serializer;

        /// <summary>
        /// Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return new ManagedConnection(Options.ConnectionFactory(), mode, isolationLevel);
        }

        /// <summary>
        /// Creates an UpdateBatch object for low level batch updates
        /// </summary>
        /// <returns></returns>
        public UpdateBatch CreateUpdateBatch()
        {
            return new UpdateBatch(Options, _serializer, OpenConnection(), new VersionTracker());
        }

        /// <summary>
        /// Creates a new Marten UnitOfWork that could be used to express
        /// a transaction
        /// </summary>
        /// <returns></returns>
        public UnitOfWork CreateUnitOfWork()
        {
            return new UnitOfWork(_schema);
        }

        /// <summary>
        /// Compiles all of the IDocumentStorage classes upfront for all known document types
        /// </summary>
        /// <returns></returns>
        public IList<IDocumentStorage> PrecompileAllStorage()
        {
            return Options.AllDocumentMappings.Select(x => _schema.StorageFor(x.DocumentType)).ToList();
        }

        /// <summary>
        /// Fetch the entity version and last modified time from the database
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public EntityMetadata MetadataFor<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, _schema.StorageFor(typeof(T)), _schema.MappingFor(typeof(T)));

            using (var connection = OpenConnection())
            {
                return connection.Fetch(handler, null);
            }
        }

    }

    public class EntityMetadata
    {
        public DateTime LastModified { get; }
        public Guid CurrentVersion { get; }

        public EntityMetadata(DateTime lastModified, Guid currentVersion)
        {
            LastModified = lastModified;
            CurrentVersion = currentVersion;
        }
    }

    public class EntityMetadataQueryHandler : IQueryHandler<EntityMetadata>
    {
        private readonly IDocumentStorage _storage;
        private readonly IDocumentMapping _mapping;
        private readonly object _id;

        public EntityMetadataQueryHandler(object entity, IDocumentStorage storage, IDocumentMapping mapping)
        {
            _id = storage.Identity(entity);
            _storage = storage;
            _mapping = mapping;
        }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql =
                $"select {DocumentMapping.VersionColumn}, {DocumentMapping.LastModifiedColumn} from {_mapping.Table.QualifiedName} where id = :id";


            command.AppendQuery(sql);

            command.AddParameter("id", _id);
        }

        public Type SourceType => _storage.DocumentType;
        public EntityMetadata Handle(DbDataReader reader, IIdentityMap map)
        {
            if (!reader.Read()) return null;

            var version = reader.GetFieldValue<Guid>(0);
            var timestamp = reader.GetFieldValue<DateTime>(1);

            return new EntityMetadata(timestamp, version);
        }

        public async Task<EntityMetadata> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasAny = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasAny) return null;

            var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTime>(1, token).ConfigureAwait(false);

            return new EntityMetadata(timestamp, version);
        }
    }
}