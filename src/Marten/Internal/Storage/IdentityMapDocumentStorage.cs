using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Storage;

/// <summary>
///     #4947 — seam that lets a nested ForTenant() session share the parent session's identity map
///     (and version state) for a *single document type* rather than all-or-nothing per session.
/// </summary>
internal interface ISharedTenantNeutralSessionState
{
    TenancyStyle TenancyStyle { get; }

    /// <summary>
    ///     Point the nested session's identity map / version entries for this document type at the
    ///     very same underlying dictionaries used by the parent session. Only ever called for
    ///     tenancy-neutral documents living in the same database.
    /// </summary>
    void ShareTenantNeutralStateWith(IStorageSession parent, IStorageSession nested);
}

public abstract class IdentityMapDocumentStorage<T, TId>: DocumentStorage<T, TId>, ISharedTenantNeutralSessionState where T: notnull where TId: notnull
{
    public IdentityMapDocumentStorage(DocumentMapping document): this(StorageStyle.IdentityMap, document)
    {
    }

    protected IdentityMapDocumentStorage(StorageStyle storageStyle, DocumentMapping document): base(storageStyle,
        document)
    {
    }

    void ISharedTenantNeutralSessionState.ShareTenantNeutralStateWith(IStorageSession parent, IStorageSession nested)
    {
        if (ReferenceEquals(parent.ItemMap, nested.ItemMap))
        {
            return;
        }

        shareIdentityMap(parent, nested);

        if (UseOptimisticConcurrency || UseNumericRevisions)
        {
            shareVersions(parent, nested);
        }
    }

    private static void shareIdentityMap(IStorageSession parent, IStorageSession nested)
    {
        if (parent.ItemMap.TryGetValue(typeof(T), out var items))
        {
            // A mismatched key type is diagnosed (and thrown on) by the normal storage paths --
            // don't propagate a broken entry into the nested session here.
            if (items is not Dictionary<TId, T>)
            {
                return;
            }
        }
        else
        {
            items = new Dictionary<TId, T>();
            parent.ItemMap[typeof(T)] = items;
        }

        nested.ItemMap[typeof(T)] = items;
    }

    private void shareVersions(IStorageSession parent, IStorageSession nested)
    {
        if (parent.Versions is not VersionTracker parentVersions ||
            nested.Versions is not VersionTracker nestedVersions)
        {
            return;
        }

        if (ReferenceEquals(parentVersions, nestedVersions))
        {
            return;
        }

        if (!parentVersions.ByType.TryGetValue(typeof(T), out var versions))
        {
            versions = UseNumericRevisions
                ? new Dictionary<TId, long>()
                : new Dictionary<TId, Guid>();

            parentVersions.ByType[typeof(T)] = versions;
        }

        nestedVersions.ByType[typeof(T)] = versions;
    }

    public sealed override void Eject(IStorageSession session, T document)
    {
        var id = Identity(document);
        if (session.ItemMap.TryGetValue(typeof(T), out var items))
        {
            if (items is Dictionary<TId, T> d)
            {
                d.Remove(id);
            }
        }
    }

    public sealed override void Store(IStorageSession session, T document)
    {
        store(session, document, out var id);
    }

    private void store(IStorageSession session, T document, out TId id)
    {
        id = AssignIdentity(document, session.TenantId, session.Database);
        session.MarkAsAddedForStorage(id, document);

        if (session.ItemMap.TryGetValue(typeof(T), out var items))
        {
            if (items is Dictionary<TId, T> d)
            {
                if (d.TryGetValue(id, out var existing))
                {
                    if (document is not IEquatable<T> && !ReferenceEquals(existing, document))
                    {
                        throw new InvalidOperationException(
                            $"Document '{typeof(T).FullNameInCode()}' with same Id already added to the session.");
                    }
                }

                d[id] = document;
            }
            else
            {
                throw new DocumentIdTypeMismatchException(typeof(T), typeof(TId));
            }
        }
        else
        {
            var dict = new Dictionary<TId, T> { { id, document } };
            session.ItemMap.Add(typeof(T), dict);
        }
    }

    public sealed override void Store(IStorageSession session, T document, Guid? version)
    {
        store(session, document, out var id);

        if (version != null)
        {
            session.Versions.StoreVersion<T, TId>(id, version.Value);
        }
        else
        {
            session.Versions.ClearVersion<T, TId>(id);
        }
    }

    public sealed override void Store(IStorageSession session, T document, long revision)
    {
        store(session, document, out var id);

        if (revision != 0)
        {
            session.Versions.StoreRevision<T, TId>(id, revision);
        }
        else
        {
            session.Versions.ClearRevision<T, TId>(id);
        }
    }

    private List<T> preselectLoadedDocuments(TId[] ids, IStorageSession session, out DbCommand command)
    {
        var list = new List<T>();

        Dictionary<TId, T> dict;
        if (session.ItemMap.TryGetValue(typeof(T), out var d))
        {
            dict = (Dictionary<TId, T>)d;
        }
        else
        {
            dict = new Dictionary<TId, T>();
            session.ItemMap.Add(typeof(T), dict);
        }

        var idList = new List<TId>();
        foreach (var id in ids)
        {
            if (dict.TryGetValue(id, out var doc))
            {
                list.Add(doc);
            }
            else
            {
                idList.Add(id);
            }
        }

        command = BuildLoadManyCommand(idList.ToArray(), session.TenantId);
        return list;
    }

    public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IStorageSession session,
        CancellationToken token)
    {
        var list = preselectLoadedDocuments(ids, session, out var command);
        var selector = (ISelector<T>)BuildSelector(session);

        await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        try
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                list.Add(document);
            }
        }
        finally
        {
            await reader.CloseAsync().ConfigureAwait(false);
        }

        return list;
    }

    public sealed override Task<T?> LoadAsync(TId id, IStorageSession session, CancellationToken token)
    {
        if (session.ItemMap.TryGetValue(typeof(T), out var items))
        {
            if (items is Dictionary<TId, T> d)
            {
                if (d.TryGetValue(id, out var item))
                {
                    return Task.FromResult<T?>(item);
                }
            }
            else
            {
                throw new DocumentIdTypeMismatchException(typeof(T), typeof(TId));
            }
        }

        return loadAsync(id, session, token);
    }
}
