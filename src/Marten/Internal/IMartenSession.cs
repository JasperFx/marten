using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Internal.DirtyTracking;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal
{
    public interface IMartenSession: IDisposable, IAsyncDisposable
    {
        ISerializer Serializer { get; }
        Dictionary<Type, object> ItemMap { get; }
        ITenant Tenant { get; }

        VersionTracker Versions { get; }

        IManagedConnection Database { get; }

        StoreOptions Options { get; }

        IList<IChangeTracker> ChangeTrackers { get; }
        IDocumentStorage StorageFor(Type documentType);


        void MarkAsAddedForStorage(object id, object document);

        void MarkAsDocumentLoaded(object id, object document);
        IDocumentStorage<T> StorageFor<T>();

        IEventStorage EventStorage();

        /// <summary>
        /// Override whether or not this session honors optimistic concurrency checks
        /// </summary>
        ConcurrencyChecks Concurrency { get; }

        string NextTempTableName();

        /// <summary>
        /// Optional metadata describing the causation id for this
        /// unit of work
        /// </summary>
        string CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id for this
        /// unit of work
        /// </summary>
        string CorrelationId { get; set; }

        /// <summary>
        /// Optional metadata describing the user name or
        /// process name for this unit of work
        /// </summary>
        string LastModifiedBy { get; set; }

        /// <summary>
        /// Optional metadata values. This may be null.
        /// </summary>
        Dictionary<string, object> Headers { get; }
    }
}
