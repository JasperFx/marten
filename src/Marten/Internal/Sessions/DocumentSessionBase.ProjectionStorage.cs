using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events.Archiving;
using Marten.Internal.Operations;
using Marten.Internal.Storage;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // TODO fix in IStorageOperations
    public async Task<IProjectionStorage<TDoc, TId>> FetchProjectionStorageAsync<TDoc, TId>(string tenantId,
        CancellationToken cancellationToken) where TDoc : notnull where TId : notnull
    {
        await Database.EnsureStorageExistsAsync(typeof(TDoc), cancellationToken).ConfigureAwait(false);
        if (tenantId == TenantId || tenantId.IsEmpty()) return new ProjectionStorage<TDoc, TId>(this, StorageFor<TDoc, TId>());

        var nested = ForTenant(tenantId);

        return new ProjectionStorage<TDoc, TId>((DocumentSessionBase)nested, StorageFor<TDoc, TId>());
    }

    public async Task<IProjectionStorage<TDoc, TId>> FetchProjectionStorageAsync<TDoc, TId>(
        CancellationToken cancellationToken) where TDoc : notnull where TId : notnull
    {
        await Database.EnsureStorageExistsAsync(typeof(TDoc), cancellationToken).ConfigureAwait(false);
        return new ProjectionStorage<TDoc, TId>(this, StorageFor<TDoc, TId>());
    }
}

internal class ProjectionStorage<TDoc, TId>: IProjectionStorage<TDoc, TId> where TId : notnull where TDoc : notnull
{
    private readonly DocumentSessionBase _session;
    private readonly IDocumentStorage<TDoc, TId> _storage;

    public ProjectionStorage(DocumentSessionBase session, IDocumentStorage<TDoc, TId> storage)
    {
        _session = session;
        _storage = storage;
    }

    public string TenantId => _session.TenantId;
    public void HardDelete(TDoc snapshot)
    {
        var deletion = _storage.HardDeleteForDocument(snapshot, TenantId);
        _session.QueueOperation(deletion);
    }

    public void UnDelete(TDoc snapshot)
    {
        throw new NotImplementedException();
    }

    public void Store(TDoc snapshot)
    {
        var upsert = _storage.Upsert(snapshot, _session, TenantId);
        _session.QueueOperation(upsert);
    }

    public void Delete(TId identity)
    {
        var deletion = _storage.DeleteForId(identity, TenantId);
        _session.QueueOperation(deletion);
    }

    public void HardDelete(TDoc snapshot, string tenantId)
    {
        var deletion = _storage.HardDeleteForDocument(snapshot, TenantId);
        _session.QueueOperation(deletion);
    }

    public void UnDelete(TDoc snapshot, string tenantId)
    {
        throw new System.NotImplementedException();
    }

    public void Store(TDoc snapshot, TId id, string tenantId)
    {
        _storage.SetIdentity(snapshot, id);
        var upsert = _storage.Upsert(snapshot, _session, tenantId);
        _session.QueueOperation(upsert);
    }

    public void Delete(TId identity, string tenantId)
    {
        var deletion = _storage.DeleteForId(identity, tenantId);
        _session.QueueOperation(deletion);
    }

    public async Task<IReadOnlyDictionary<TId, TDoc>> LoadManyAsync(TId[] identities, CancellationToken cancellationToken)
    {
        var docs = await _storage.LoadManyAsync(identities, _session, cancellationToken).ConfigureAwait(false);
        return docs.ToDictionary(doc => _storage.Identity(doc));
    }

    public void SetIdentity(TDoc document, TId identity)
    {
        _storage.SetIdentity(document, identity);
    }

    public void StoreProjection(TDoc aggregate, IEvent lastEvent, AggregationScope scope)
    {
        var op = _storage.Overwrite(aggregate, _session, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = scope == AggregationScope.SingleStream ? (int)lastEvent.Version : (int)lastEvent.Sequence;
            r.IgnoreConcurrencyViolation = true;
        }

        _session.QueueOperation(op);
    }

    public void ArchiveStream(TId sliceId, string tenantId)
    {
        var op = archiveOperationBuilderFor<TId>()(sliceId);
        op.TenantId = tenantId;

        _session.QueueOperation(op);
    }

    private static ImHashMap<Type, object> _archiveBuilders = ImHashMap<Type, object>.Empty;

    private Func<TId, ArchiveStreamOperation> archiveOperationBuilderFor<TId>()
    {
        if (_archiveBuilders.TryFind(typeof(TId), out var raw))
        {
            return (Func<TId, ArchiveStreamOperation>)raw;
        }

        Func<TId, ArchiveStreamOperation> builder = null;
        if (_session.Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            if (typeof(TId) == typeof(Guid))
            {
                builder = id => new ArchiveStreamOperation(_session.Options.EventGraph, id);
            }
            else
            {
                var valueType = ValueTypeInfo.ForType(typeof(TId));
                var unWrapper = valueType.UnWrapper<TId, Guid>();
                builder = id =>  new ArchiveStreamOperation(_session.Options.EventGraph, unWrapper(id));
            }
        }
        else
        {
            if (typeof(TId) == typeof(string))
            {
                builder = id => new ArchiveStreamOperation(_session.Options.EventGraph, id);
            }
            else
            {
                var valueType = ValueTypeInfo.ForType(typeof(TId));
                var unWrapper = valueType.UnWrapper<TId, string>();
                builder = id =>  new ArchiveStreamOperation(_session.Options.EventGraph, unWrapper(id));
            }
        }

        _archiveBuilders = _archiveBuilders.AddOrUpdate(typeof(TId), builder);
        return builder;
    }

    //TODO fix in IProjectionStorage
    public Task<TDoc?> LoadAsync(TId id, CancellationToken cancellation)
    {
        return _storage.LoadAsync(id, _session, cancellation);
    }
}
