#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Services;

namespace Marten;

/// <summary>
///     Interface for querying a document database and unit of work updates
/// </summary>
public interface IDocumentSession: IDocumentOperations
{
    /// <summary>
    /// Force this session to start a connection and transaction. This will make the session do consistent reads through the transaction. Is
    /// idempotent.
    /// </summary>
    void BeginTransaction();

    /// <summary>
    /// Force this session to start a connection and transaction. This will make the session do consistent reads through the transaction. Is
    /// idempotent.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    ValueTask BeginTransactionAsync(CancellationToken token);

    /// <summary>
    ///     List of all the pending changes to this IDocumentSession
    /// </summary>
    IUnitOfWork PendingChanges { get; }

    /// <summary>
    ///     Access to the event store functionality
    /// </summary>
    new IEventStoreOperations Events { get; }

    /// <summary>
    ///     Override whether or not this session honors optimistic concurrency checks
    /// </summary>
    ConcurrencyChecks Concurrency { get; }

    /// <summary>
    ///     Writeable list of the listeners for this session
    /// </summary>
    IList<IDocumentSessionListener> Listeners { get; }

    /// <summary>
    ///     Optional metadata describing the user name or
    ///     process name for this unit of work
    /// </summary>
    string? LastModifiedBy { get; set; }

    /// <summary>
    ///     Asynchronously saves all the pending changes and deletions to the server in a single Postgresql transaction
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    Task SaveChangesAsync(CancellationToken token = default);

    /// <summary>
    ///     Completely remove the document from this session's unit of work tracking and identity map caching
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="document"></param>
    void Eject<T>(T document) where T : notnull;

    /// <summary>
    ///     Completely remove all the documents of given type from this session's unit of work tracking and identity map
    ///     caching
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="type"></param>
    void EjectAllOfType(Type type);

    /// <summary>
    ///     Set an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    void SetHeader(string key, object value);

    /// <summary>
    ///     Get an optional user defined metadata value by key
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    object? GetHeader(string key);

    /// <summary>
    ///     Access data from another tenant and apply document or event updates to this
    ///     IDocumentSession for a separate tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    new ITenantOperations ForTenant(string tenantId);

    /// <summary>
    ///     Remove all queued operations in this unit of work, but will not impact any existing identity map. I.e., all
    ///     document stores,
    ///     event appends, etc.
    /// </summary>
    void EjectAllPendingChanges();
}

public interface ILoadByKeys<TDoc>
{
    /// <summary>
    ///     Supply the document id's to load
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="keys"></param>
    /// <returns></returns>
    IReadOnlyList<TDoc> ById<TKey>(params TKey[] keys);

    /// <summary>
    ///     Supply the document id's to load asynchronously
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="keys"></param>
    /// <returns></returns>
    Task<IReadOnlyList<TDoc>> ByIdAsync<TKey>(params TKey[] keys);

    /// <summary>
    ///     Supply the document id's to load
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="keys"></param>
    /// <returns></returns>
    IReadOnlyList<TDoc> ById<TKey>(IEnumerable<TKey> keys);

    /// <summary>
    ///     Supply the document id's to load asynchronously
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="keys"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IReadOnlyList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default);
}
