using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly IDocumentSchema _schema;

        public AdvancedOptions(IDocumentCleaner cleaner, StoreOptions options, ISerializer serializer,
            IDocumentSchema schema)
        {
            Serializer = serializer;
            _schema = schema;
            Options = options;
            Clean = cleaner;
        }

        /// <summary>
        ///     The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        public StoreOptions Options { get; }

        /// <summary>
        ///     Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean { get; }


        public ISerializer Serializer { get; }

        /// <summary>
        ///     Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return new ManagedConnection(Options.ConnectionFactory(), mode, isolationLevel);
        }

        /// <summary>
        ///     Creates an UpdateBatch object for low level batch updates
        /// </summary>
        /// <returns></returns>
        public UpdateBatch CreateUpdateBatch()
        {
            return new UpdateBatch(Options, Serializer, OpenConnection(), new VersionTracker());
        }

        /// <summary>
        ///     Creates a new Marten UnitOfWork that could be used to express
        ///     a transaction
        /// </summary>
        /// <returns></returns>
        public UnitOfWork CreateUnitOfWork()
        {
            return new UnitOfWork(_schema);
        }

        /// <summary>
        ///     Compiles all of the IDocumentStorage classes upfront for all known document types
        /// </summary>
        /// <returns></returns>
        public IList<IDocumentStorage> PrecompileAllStorage()
        {
            return Options.AllDocumentMappings.Select(x => _schema.StorageFor(x.DocumentType)).ToList();
        }

        /// <summary>
        ///     Fetch the entity version and last modified time from the database
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public DocumentMetadata MetadataFor<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, _schema.StorageFor(typeof(T)),
                _schema.MappingFor(typeof(T)));

            using (var connection = OpenConnection())
            {
                return connection.Fetch(handler, null, null);
            }
        }

        /// <summary>
        ///     Fetch the entity version and last modified time from the database
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<DocumentMetadata> MetadataForAsync<T>(T entity, CancellationToken token = default(CancellationToken))
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var handler = new EntityMetadataQueryHandler(entity, _schema.StorageFor(typeof(T)),
                _schema.MappingFor(typeof(T)));

            using (var connection = OpenConnection())
            {
                return await connection.FetchAsync(handler, null, null, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Set the minimum sequence number for a Hilo sequence for a specific document type
        /// to the specified floor. Useful for migrating data between databases 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="floor"></param>
        public void ResetHiloSequenceFloor<T>(long floor)
        {
            // Make sure that the sequence is built for this one
            _schema.IdAssignmentFor<T>();
            var sequence = _schema.Sequences.SequenceFor(typeof(T));
            sequence.SetFloor(floor);
        }
    }

    public class DocumentMetadata
    {
        public DocumentMetadata(DateTime lastModified, Guid currentVersion, string dotNetType, string documentType, bool deleted, DateTime? deletedAt)
        {
            LastModified = lastModified;
            CurrentVersion = currentVersion;
            DotNetType = dotNetType;
            DocumentType = documentType;
            Deleted = deleted;
            DeletedAt = deletedAt;
        }

        public DateTime LastModified { get; }
        public Guid CurrentVersion { get; }
        public string DotNetType { get; }
        public string DocumentType { get; }
        public bool Deleted { get; }
        public DateTime? DeletedAt { get; }
    }

    public class EntityMetadataQueryHandler : IQueryHandler<DocumentMetadata>
    {
        private readonly object _id;
        private readonly IDocumentStorage _storage;
        private readonly IDocumentMapping _mapping;
        private readonly Dictionary<string, int> _fields;

        public EntityMetadataQueryHandler(object entity, IDocumentStorage storage, IDocumentMapping mapping)
        {
            _id = storage.Identity(entity);
            _storage = storage;
            _mapping = mapping;

            var fieldIndex = 0;
            _fields = new Dictionary<string, int>
            {
                {DocumentMapping.VersionColumn, fieldIndex++},
                {DocumentMapping.LastModifiedColumn, fieldIndex++},
                {DocumentMapping.DotNetTypeColumn, fieldIndex++}
            };
            var queryableDocument = _mapping.ToQueryableDocument();
            if (queryableDocument.SelectFields().Contains(DocumentMapping.DocumentTypeColumn))
            {
                _fields.Add(DocumentMapping.DocumentTypeColumn, fieldIndex++);
            }
            if (queryableDocument.DeleteStyle == DeleteStyle.SoftDelete)
            {
                _fields.Add(DocumentMapping.DeletedColumn, fieldIndex++);
                _fields.Add(DocumentMapping.DeletedAtColumn, fieldIndex);
            }
        }

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = "select " +
                      string.Join(", ", _fields.OrderBy(kv => kv.Value).Select(kv => kv.Key)) +
                      $" from {_mapping.Table.QualifiedName} where id = :id";

            command.AppendQuery(sql);

            command.AddParameter("id", _id);
        }

        public Type SourceType => _storage.DocumentType;

        public DocumentMetadata Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            if (!reader.Read()) return null;

            var version = reader.GetFieldValue<Guid>(0);
            var timestamp = reader.GetFieldValue<DateTime>(1);
            var dotNetType = reader.GetFieldValue<string>(2);
            var docType = GetOptionalFieldValue<string>(reader, DocumentMapping.DocumentTypeColumn);
            var deleted = GetOptionalFieldValue<bool>(reader, DocumentMapping.DeletedColumn);
            var deletedAt = GetOptionalFieldValue<DateTime>(reader, DocumentMapping.DeletedAtColumn, null);

            return new DocumentMetadata(timestamp, version, dotNetType, docType, deleted, deletedAt);
        }

        private T GetOptionalFieldValue<T>(DbDataReader reader, string fieldName)
        {
            int ordinal;
            if (_fields.TryGetValue(fieldName, out ordinal) && !reader.IsDBNull(ordinal))
                return reader.GetFieldValue<T>(ordinal);
            return default(T);
        }

        private T? GetOptionalFieldValue<T>(DbDataReader reader, string fieldName, T? defaultValue) where T : struct
        {
            int ordinal;
            if (_fields.TryGetValue(fieldName, out ordinal) && !reader.IsDBNull(ordinal))
                return reader.GetFieldValue<T>(ordinal);
            return defaultValue;
        }

        public async Task<DocumentMetadata> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var hasAny = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasAny) return null;

            var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTime>(1, token).ConfigureAwait(false);
            var dotNetType = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            var docType = await GetOptionalFieldValueAsync<string>(reader, DocumentMapping.DocumentTypeColumn, token).ConfigureAwait(false);
            var deleted = await GetOptionalFieldValueAsync<bool>(reader, DocumentMapping.DeletedColumn, token).ConfigureAwait(false);
            var deletedAt = await GetOptionalFieldValueAsync<DateTime>(reader, DocumentMapping.DeletedAtColumn, null, token).ConfigureAwait(false);

            return new DocumentMetadata(timestamp, version, dotNetType, docType, deleted, deletedAt);
        }

        private async Task<T> GetOptionalFieldValueAsync<T>(DbDataReader reader, string fieldName, CancellationToken token)
        {
            int ordinal;
            if (_fields.TryGetValue(fieldName, out ordinal) && !await reader.IsDBNullAsync(ordinal, token))
                return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
            return default(T);
        }

        private async Task<T?> GetOptionalFieldValueAsync<T>(DbDataReader reader, string fieldName, T? defaultValue, CancellationToken token) where T : struct
        {
            int ordinal;
            if (_fields.TryGetValue(fieldName, out ordinal) && !await reader.IsDBNullAsync(ordinal, token))
                return await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);
            return defaultValue;
        }
    }
}
