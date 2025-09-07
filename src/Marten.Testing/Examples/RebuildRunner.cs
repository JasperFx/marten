using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Aggregation;

namespace Marten.Testing.Examples;

public record Shop(Guid Id, List<string> Items);
public record ShopCreated(Guid Id, List<string> Items);

#region sample_rebuild-shop_projection
public class ShopProjection: SingleStreamProjection<Guid, Shop>
{
    public ShopProjection()
    {
        Name = "Shop";
    }

    // Create a new Shop document based on a CreateShop event
    public Shop Create(ShopCreated @event)
    {
        return new Shop(@event.Id, @event.Items);
    }
}
#endregion

public class RebuildRunner
{
    #region sample_rebuild-single-projection
    private IDocumentStore _store;

    public RebuildRunner(IDocumentStore store)
    {
        _store = store;
    }

    public async Task RunRebuildAsync()
    {
        using var daemon = await _store.BuildProjectionDaemonAsync();

        await daemon.RebuildProjectionAsync("Shop", CancellationToken.None);
    }
    #endregion
}
