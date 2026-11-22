#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ImTools;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

/// <summary>
/// Node-local pointers from a natural key to the stream id an aggregate write cache entry is held under,
/// so that a fetch by natural key can take that entry the way a fetch by stream id does.
/// </summary>
/// <remarks>
/// <para>
/// Pointers, not a second copy of the aggregate. <see cref="IAggregateWriteCache.TryTake" /> hands a
/// mutable aggregate to exactly one caller, and an entry stored under both the stream id and the natural
/// key could be taken through each by two callers at once.
/// </para>
/// <para>
/// A pointer is only ever a hint. The fetch still resolves the key in the database, in the same round
/// trip, and only uses the cached snapshot when the database names the same stream. A stale pointer --
/// the key renamed, retired or claimed by another stream since -- therefore costs a redo, never a wrong
/// aggregate. Pointers are registered whenever an aggregate is written back to the cache, whichever way
/// it was fetched, and bounded by the cache's size limit; a dropped pointer just means a cache miss.
/// </para>
/// </remarks>
internal sealed class NaturalKeyStreamIndex
{
    private readonly EventGraph _events;
    private readonly RecentlyUsedCache<Pointer, object> _pointers = new();
    private ImHashMap<Type, NaturalKeyDefinition[]> _keysByType = ImHashMap<Type, NaturalKeyDefinition[]>.Empty;

    public NaturalKeyStreamIndex(EventGraph events, int sizeLimit)
    {
        _events = events;
        _pointers.Limit = sizeLimit;
    }

    /// <summary>
    /// The stream id <paramref name="keyValue" /> (unwrapped) last pointed at, for this aggregate type,
    /// database and tenant.
    /// </summary>
    public bool TryFind(NaturalKeyDefinition key, AggregateCacheKey scope, object keyValue,
        [NotNullWhen(true)] out object? streamId)
    {
        return _pointers.TryFind(pointerFor(key, scope, keyValue), out streamId);
    }

    public void Forget(NaturalKeyDefinition key, AggregateCacheKey scope, object keyValue)
    {
        _pointers.TryRemove(pointerFor(key, scope, keyValue));
    }

    /// <summary>
    /// Point every natural key <paramref name="aggregate" /> currently has at the stream it is cached under.
    /// </summary>
    public void Register(AggregateCacheKey cacheKey, object aggregate)
    {
        foreach (var key in keysFor(cacheKey.DocumentType))
        {
            var value = key.ReadValue(aggregate);
            if (value == null) continue;

            _pointers.Store(pointerFor(key, cacheKey, value), cacheKey.Id);
        }

        _pointers.CompactIfNecessary();
    }

    private static Pointer pointerFor(NaturalKeyDefinition key, AggregateCacheKey scope, object keyValue)
        => new(scope.DocumentType, key.OuterType, scope.DatabaseIdentifier, scope.TenantId, keyValue);

    private NaturalKeyDefinition[] keysFor(Type documentType)
    {
        if (_keysByType.TryFind(documentType, out var keys)) return keys;

        keys = _events.Options.Projections.TryFindAggregate(documentType, out var projection)
            ? projection.MappedNaturalKeyDefinitions
                .Concat(projection.NaturalKeyDefinition == null ? [] : [projection.NaturalKeyDefinition])
                .ToArray()
            : [];

        _keysByType = _keysByType.AddOrUpdate(documentType, keys);
        return keys;
    }

    /// <summary>
    /// Keys on one aggregate have distinct types, so the key type tells them apart.
    /// </summary>
    private readonly record struct Pointer(
        Type DocumentType,
        Type KeyType,
        string DatabaseIdentifier,
        string TenantId,
        object KeyValue);
}
