using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;

namespace Marten;

internal class EventStoreCapability: IEventStoreCapability
{
    private readonly DocumentStore _store;

    public EventStoreCapability(IDocumentStore store)
    {
        _store = (DocumentStore)store;
    }

    protected virtual Type storeType() => typeof(IDocumentStore);

    public async Task<EventStoreUsage> TryCreateUsage(CancellationToken token)
    {
        if (!_store.Options.EventGraph.IsActive(_store.Options))
        {
            return null;
        }

        return await _store.Options.DescribeEventUsage(storeType(), token).ConfigureAwait(false);
    }

    // public bool TryCreateUsage(out EventStoreUsage usage)
    // {
    //     if (!_store.Options.EventGraph.IsActive(_store.Options))
    //     {
    //         usage = default!;
    //         return false;
    //     }
    //
    //     usage = _store.Options.DescribeEventUsage(storeType());
    //
    //     return true;
    // }
}

internal class EventStoreCapability<T> : EventStoreCapability where T : IDocumentStore
{
    public EventStoreCapability(T store) : base(store)
    {
    }

    protected override Type storeType()
    {
        return typeof(T);
    }
}


public partial class StoreOptions
{
    internal async Task<EventStoreUsage> DescribeEventUsage(Type storeType, CancellationToken token)
    {
        var usage = new EventStoreUsage(storeType, this)
        {
            Database = await Tenancy.DescribeDatabasesAsync(token).ConfigureAwait(false)
        };

        foreach (var eventMapping in EventGraph.AllEvents())
        {
            var descriptor =
                new EventDescriptor(eventMapping.EventTypeName, TypeDescriptor.For(eventMapping.DocumentType));

            usage.Events.Add(descriptor);
        }

        foreach (var projection in Projections.All)
        {

        }

        return usage;
    }
}
