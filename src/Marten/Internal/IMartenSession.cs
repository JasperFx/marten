using System;
using System.Collections.Generic;
using Marten.Internal.DirtyTracking;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal
{
    public interface IMartenSession: IDisposable
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

        /// <summary>
        /// Override whether or not this session honors optimistic concurrency checks
        /// </summary>
        ConcurrencyChecks Concurrency { get; }

        string NextTempTableName();
    }
}
