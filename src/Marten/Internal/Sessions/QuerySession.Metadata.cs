
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage.Metadata;

#nullable enable
namespace Marten.Internal.Sessions
{
    public partial class QuerySession
    {
        public VersionTracker Versions { get; internal set; } = new();

        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string? LastModifiedBy { get; set; }

        /// <summary>
        ///     This is meant to be lazy created, and can be null
        /// </summary>
        public Dictionary<string, object>? Headers { get; protected set; }

        public Guid? VersionFor<TDoc>(TDoc entity) where TDoc : notnull
        {
            return StorageFor<TDoc>().VersionFor(entity, this);
        }

        public DocumentMetadata? MetadataFor<T>(T entity) where T : notnull
        {
            assertNotDisposed();
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            Database.EnsureStorageExists(typeof(T));

            var storage = StorageFor<T>();
            var id = storage.IdentityFor(entity);
            var handler = new EntityMetadataQueryHandler(id, storage);

            return ExecuteHandler(handler);
        }

        public async Task<DocumentMetadata> MetadataForAsync<T>(T entity, CancellationToken token = default) where T : notnull
        {
            assertNotDisposed();
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var storage = StorageFor<T>();
            await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
            var id = storage.IdentityFor(entity);
            var handler = new EntityMetadataQueryHandler(id, storage);

            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }

    }
}
