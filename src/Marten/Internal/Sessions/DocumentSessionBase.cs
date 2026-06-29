using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Metadata;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal.Sessions;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public abstract partial class DocumentSessionBase: QuerySession, IDocumentSession, ITransactionParticipantRegistrar
{
    internal readonly ISessionWorkTracker _workTracker;
    private readonly List<ITransactionParticipant> _transactionParticipants = new();

    IServiceProvider? IStorageOperations.Services => Options.Services;

    private Dictionary<string, NestedTenantSession>? _byTenant;

    internal DocumentSessionBase(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection): base(store, sessionOptions, connection)
    {
        Concurrency = sessionOptions.ConcurrencyChecks;
        _workTracker = new UnitOfWork(this);
    }

    internal DocumentSessionBase(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection,
        ISessionWorkTracker workTracker, Tenant? tenant = default
    ): base(store, sessionOptions, connection, tenant)
    {
        Concurrency = sessionOptions.ConcurrencyChecks;
        _workTracker = workTracker;
    }

    internal ITenancy Tenancy => DocumentStore.As<DocumentStore>().Tenancy;

    internal ISessionWorkTracker WorkTracker => _workTracker;

    public virtual void AddTransactionParticipant(ITransactionParticipant participant)
    {
        _transactionParticipants.Add(participant);
    }

    internal IReadOnlyList<ITransactionParticipant> TransactionParticipants => _transactionParticipants;

    public void EjectAllPendingChanges()
    {
        _workTracker.EjectAll();
        ChangeTrackers.Clear();
    }

    /// <summary>
    /// #4685 PR 2 (proving the blocker): the shard execution mode this session is operating
    /// under when it backs an async projection / subscription apply. Defaults to
    /// <see cref="ShardExecutionMode.Continuous"/> for ordinary sessions;
    /// <see cref="Marten.Events.Daemon.Internals.ProjectionDocumentSession"/> overrides it with the
    /// daemon's per-shard mode, and <see cref="NestedTenantSession"/> delegates to its parent.
    /// <see cref="ProjectionStorage{TDoc,TId}"/> reads it to decide whether a post-teardown rebuild
    /// replay can be routed through INSERT-only operations (the BulkWriter COPY win) instead of UPSERT.
    /// </summary>
    internal virtual ShardExecutionMode ExecutionMode => ShardExecutionMode.Continuous;

    public void Store<T>(IEnumerable<T> entities) where T : notnull
    {
        Store(entities?.ToArray()!);
    }

    public void Store<T>(params T[] entities) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. Either cast entities to an array instead or use the IEnumerable<T> Store() overload instead.");
        }

        store(entities);
    }

    public void UpdateExpectedVersion<T>(T entity, Guid version) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, version);
        var op = storage.Upsert(entity, this, TenantId);
        _workTracker.Add(op);
    }

    public void UpdateRevision<T>(T entity, long revision) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, revision);
        var op = storage.Upsert(entity, this, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = revision;
        }

        _workTracker.Add(op);
    }

    public void TryUpdateRevision<T>(T entity, long revision) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, revision);
        var op = storage.Upsert(entity, this, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = revision;
            r.IgnoreConcurrencyViolation = true;
        }

        _workTracker.Add(op);
    }

    public void Insert<T>(IEnumerable<T> entities) where T : notnull
    {
        Insert(entities.ToArray());
    }

    public void Insert<T>(params T[] entities) where T : notnull
    {
        assertNotDisposed();

        ArgumentNullException.ThrowIfNull(entities);

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
        }

        if (typeof(T) == typeof(object))
        {
            InsertObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            foreach (var entity in entities)
            {
                storage.Store(this, entity);
                var op = storage.Insert(entity, this, TenantId);
                if (op is IRevisionedOperation r)
                {
                    r.Revision = 1;
                }

                _workTracker.Add(op);
            }
        }
    }

    public void Update<T>(IEnumerable<T> entities) where T : notnull
    {
        Update(entities.ToArray());
    }

    public void Update<T>(params T[] entities) where T : notnull
    {
        assertNotDisposed();

        ArgumentNullException.ThrowIfNull(entities);

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
        }

        if (typeof(T) == typeof(object))
        {
            InsertObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            if (Concurrency == ConcurrencyChecks.Disabled && storage.UseOptimisticConcurrency)
            {
                foreach (var entity in entities)
                {
                    storage.Store(this, entity);
                    var op = storage.Update(entity, this, TenantId);
                    _workTracker.Add(op);
                }
            }
            else
            {
                foreach (var entity in entities)
                {
                    storeEntity(entity, storage);
                    var op = storage.Update(entity, this, TenantId);
                    _workTracker.Add(op);
                }
            }
        }
    }

    public void InsertObjects(IEnumerable<object> documents)
    {
        assertNotDisposed();

        documents.Where(x => x != null).GroupBy(x => x.GetType()).Each(group =>
        {
            // 9.0 (#4373): delegate-cached factory keyed on document type.
            var handler = GenericFactoryCache.BuildAs<IObjectHandler>(
                typeof(InsertHandler<>),
                group.Key,
                static closed => () => (IObjectHandler)Activator.CreateInstance(closed)!);
            handler.Execute(this, group);
        });
    }

    public void QueueSqlCommand(string sql, params object[] parameterValues)
    {
        QueueSqlCommand(DefaultParameterPlaceholder, sql, parameterValues);
    }

    public void QueueSqlCommand(char placeholder, string sql, params object[] parameterValues)
    {
        sql = sql.TrimEnd(';');
        if (sql.Contains(';'))
        {
            throw new ArgumentOutOfRangeException(nameof(sql),
                "You must specify one SQL command at a time because of Marten's usage of command batching. ';' cannot be used as a command separator here.");
        }

        var operation = new ExecuteSqlStorageOperation(placeholder, sql, parameterValues);
        QueueOperation(operation);
    }

    public IUnitOfWork PendingChanges => _workTracker;

    public void StoreObjects(IEnumerable<object> documents)
    {
        assertNotDisposed();

        var documentsGroupedByType = documents
            .Where(x => x != null)
            .GroupBy(x => x.GetType());

        foreach (var group in documentsGroupedByType)
        {
            // 9.0 (#4373): delegate-cached factory keyed on document type.
            var handler = GenericFactoryCache.BuildAs<IObjectHandler>(
                typeof(StoreHandler<>),
                group.Key,
                static closed => () => (IObjectHandler)Activator.CreateInstance(closed)!);
            handler.Execute(this, group);
        }
    }

    public new Marten.Events.IEventStoreOperations Events => (Marten.Events.IEventStoreOperations)base.Events;


    public void QueueOperation(IStorageOperation storageOperation)
    {
        _workTracker.Add(storageOperation);
    }

    public virtual void Eject<T>(T document) where T : notnull
    {
        StorageFor<T>().Eject(this, document);
        _workTracker.Eject(document);

        ChangeTrackers.RemoveAll(x => ReferenceEquals(document, x.Document));
    }

    public virtual void EjectAllOfType(Type type)
    {
        ItemMap.Remove(type);

        _workTracker.EjectAllOfType(type);

        ChangeTrackers.RemoveAll(x => x.Document.GetType().CanBeCastTo(type));
    }

    /// <summary>
    /// Lazily-computed per-session cache of the JSON-encoded <see cref="Headers"/> dictionary,
    /// invalidated whenever <see cref="SetHeader"/> mutates the contents. Storage operations
    /// that bind the headers as a JSONB parameter use this to skip re-serializing on every
    /// queued op in a batch — under load a single SaveChanges with N tagged operations can
    /// otherwise serialize the same dictionary N times. See <see cref="GetCachedSerializedHeaders"/>.
    /// </summary>
    private byte[]? _serializedHeadersCache;

    public void SetHeader(string key, object value)
    {
        Headers ??= new Dictionary<string, object>();

        Headers[key] = value;
        _serializedHeadersCache = null; // invalidate cached bytes
    }

    /// <summary>
    /// Returns the JSON bytes for the session's current <see cref="Headers"/> dictionary,
    /// computing and caching on first call. Returns <c>null</c> when there are no headers.
    /// </summary>
    internal byte[]? GetCachedSerializedHeaders()
    {
        if (Headers is null || Headers.Count == 0) return null;

        if (_serializedHeadersCache is not null) return _serializedHeadersCache;

        using var buffer = new Services.PooledByteBufferWriter();
        Serializer.WriteTo(buffer, Headers);
        _serializedHeadersCache = buffer.ToSizedArray();
        return _serializedHeadersCache;
    }

    public object? GetHeader(string key)
    {
        return Headers?.TryGetValue(key, out var value) ?? false ? value : null;
    }

    /// <summary>
    ///     Access data from another tenant and apply document or event updates to this
    ///     IDocumentSession for a separate tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    public new ITenantOperations ForTenant(string tenantId)
    {
        _byTenant ??= new Dictionary<string, NestedTenantSession>();

        if (_byTenant.TryGetValue(tenantId, out var tenantSession))
        {
            return tenantSession;
        }

        var isValid = DocumentStore.Options.Tenancy.IsTenantStoredInCurrentDatabase(Database, tenantId);
        if (!isValid)
        {
            throw new InvalidTenantForDatabaseException(tenantId, Database);
        }

        var tenant = new Tenant(tenantId, Database);
        tenantSession = new NestedTenantSession(this, tenant);
        _byTenant[tenantId] = tenantSession;

        return tenantSession;
    }

    protected override Marten.Events.IQueryEventStore CreateEventStore(DocumentStore store, Tenant tenant)
    {
        return new EventStore(this, store, tenant);
    }

    protected internal abstract void ejectById<T>(long id) where T : notnull;
    protected internal abstract void ejectById<T>(int id) where T : notnull;
    protected internal abstract void ejectById<T>(Guid id) where T : notnull;
    protected internal abstract void ejectById<T>(string id) where T : notnull;

    protected internal virtual void processChangeTrackers()
    {
        // Nothing
    }

    protected internal virtual void resetDirtyChecking()
    {
        // Nothing
    }

    private void store<T>(IEnumerable<T> entities) where T : notnull
    {
        assertNotDisposed();

        if (typeof(T) == typeof(object))
        {
            StoreObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            if (Concurrency == ConcurrencyChecks.Disabled &&
                (storage.UseOptimisticConcurrency || storage.UseNumericRevisions))
            {
                foreach (var entity in entities)
                {
                    // Put it in the identity map -- if necessary
                    storage.Store(this, entity);

                    var overwrite = storage.Overwrite(entity, this, TenantId);

                    _workTracker.Add(overwrite);
                }
            }
            else
            {
                foreach (var entity in entities)
                {
                    storeEntity(entity, storage);

                    var upsert = storage.Upsert(entity, this, TenantId);

                    _workTracker.Add(upsert);
                }
            }
        }
    }

    private void storeEntity<T>(T entity, IDocumentStorage<T> storage) where T : notnull
    {
        switch (entity)
        {
            case IVersioned versioned when versioned.Version != Guid.Empty:
                storage.Store(this, entity, versioned.Version);
                return;
            case IRevisioned revisioned when revisioned.Version != 0:
                storage.Store(this, entity, revisioned.Version);
                return;
            case ILongVersioned longVersioned when longVersioned.Version != 0:
                storage.Store(this, entity, longVersioned.Version);
                return;
            default:
                // Put it in the identity map -- if necessary
                storage.Store(this, entity);
                break;
        }
    }

    public void EjectPatchedTypes(IUnitOfWork changes)
    {
        var patchedTypes = changes.Operations().Where(x => x.Role() == OperationRole.Patch).Select(x => x.DocumentType)
            .Distinct().ToArray();
        foreach (var type in patchedTypes) EjectAllOfType(type);
    }

    internal void StoreDocumentInItemMap<TDoc, TId>(TId id, TDoc document) where TDoc : class where TId : notnull
    {
        if (ItemMap.TryGetValue(typeof(TDoc), out var existing))
        {
            if (existing is Dictionary<TId, TDoc> typedDict)
            {
                typedDict[id] = document;
            }
            // else: The identity map was created with a different key type (e.g., a strong-typed ID
            // like PaymentId while TId is Guid). The document is already stored by the inline
            // projection under the strong-typed key, so we skip storing it again to avoid
            // replacing the dictionary with an incompatible key type.
        }
        else
        {
            var dict = new Dictionary<TId, TDoc>();
            dict[id] = document;
            ItemMap[typeof(TDoc)] = dict;
        }
    }

    internal bool TryGetAggregateFromIdentityMap<TDoc, TId>(TId id, [NotNullWhen(true)] out TDoc? document)
        where TDoc : notnull where TId : notnull
    {
        if (Options.EventGraph.UseIdentityMapForAggregates)
        {
            if (ItemMap.TryGetValue(typeof(TDoc), out var raw))
            {
                if (raw is Dictionary<TId, TDoc> dict)
                {
                    if (dict.TryGetValue(id, out var doc))
                    {
                        document = doc;
                        return true;
                    }
                }
            }
        }

        document = default;
        return false;
    }

    internal void EjectAggregateFromIdentityMap<TDoc, TId>(TId id)
        where TDoc : notnull where TId : notnull
    {
        if (ItemMap.TryGetValue(typeof(TDoc), out var raw) && raw is Dictionary<TId, TDoc> dict)
        {
            dict.Remove(id);
        }
    }

    internal interface IObjectHandler
    {
        void Execute(IDocumentSession session, IEnumerable<object> objects);
    }

    internal class StoreHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            // Delegate to the Store<T>() method
            session.Store(objects.OfType<T>().ToArray());
        }
    }

    internal class InsertHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            session.Insert(objects.OfType<T>().ToArray());
        }
    }

    internal class DeleteHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            foreach (var document in objects.OfType<T>()) session.Delete(document);
        }
    }
}
