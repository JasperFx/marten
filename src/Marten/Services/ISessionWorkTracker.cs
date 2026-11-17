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
    IReadOnlyList<Weasel.Storage.IStorageOperation> AllOperations { get; }
    void Reset();
    void Add(Weasel.Storage.IStorageOperation operation);
    void Sort(StoreOptions options);
    void Eject<T>(T document) where T : notnull;
    void EjectAllOfType(Type type);
    /// <summary>
    ///     Find the pending stream action for this stream. #6031: under conjoined tenancy a stream id is only
    ///     unique within a tenant, so pass the tenant to match on; null matches any tenant.
    /// </summary>
    bool TryFindStream(string streamKey, string? tenantId, [NotNullWhen(true)]out StreamAction? stream);

    /// <inheritdoc cref="TryFindStream(string, string?, out StreamAction?)"/>
    bool TryFindStream(Guid streamId, string? tenantId, [NotNullWhen(true)]out StreamAction? stream);
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
