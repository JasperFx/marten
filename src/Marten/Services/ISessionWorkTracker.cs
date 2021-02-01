using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Internal.Operations;

namespace Marten.Services
{
    internal interface ISessionWorkTracker: IUnitOfWork, IChangeSet
    {
        void Reset();
        void Add(IStorageOperation operation);
        void Sort(StoreOptions options);
        new List<StreamAction> Streams { get; }
        IReadOnlyList<IStorageOperation> AllOperations { get; }
        void Eject<T>(T document);
        bool TryFindStream(string streamKey, out StreamAction stream);
        bool TryFindStream(Guid streamId, out StreamAction stream);
        bool HasOutstandingWork();
    }
}
