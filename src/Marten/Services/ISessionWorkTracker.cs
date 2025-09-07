using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events;
using Marten.Events;
using Marten.Internal.Operations;

namespace Marten.Services;

internal interface ISessionWorkTracker: IUnitOfWork, IChangeSet
{
    new List<StreamAction> Streams { get; }
    IReadOnlyList<IStorageOperation> AllOperations { get; }
    void Reset();
    void Add(IStorageOperation operation);
    void Sort(StoreOptions options);
    void Eject<T>(T document) where T : notnull;
    void EjectAllOfType(Type type);
    bool TryFindStream(string streamKey, [NotNullWhen(true)]out StreamAction? stream);
    bool TryFindStream(Guid streamId, [NotNullWhen(true)]out StreamAction? stream);
    bool HasOutstandingWork();
    void EjectAll();

    /// <summary>
    /// Remove all outstanding operations for the designated document
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="TId"></typeparam>
    void PurgeOperations<T, TId>(TId id) where T : notnull where TId : notnull;
}
