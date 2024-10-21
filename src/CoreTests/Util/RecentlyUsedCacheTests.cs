using System;
using System.Collections.Generic;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;

namespace CoreTests.Util;

public class RecentlyUsedCacheTests
{
    private readonly RecentlyUsedCache<Guid, Item> theCache = new (){Limit = 100};

    [Fact]
    public void get_the_same_value_back()
    {
        var items = new List<Item>();
        for (int i = 0; i < 10; i++)
        {
            var item = new Item(Guid.NewGuid());
            theCache.Store(item.Id, item);
            items.Add(item);
        }

        foreach (var item in items)
        {
            theCache.TryFind(item.Id, out var found).ShouldBeTrue();
            found.ShouldBeSameAs(item);
        }
    }

    [Fact]
    public void compact_moves_off_the_first_ones()
    {
        var items = new List<Item>();
        for (int i = 0; i < 110; i++)
        {
            var item = new Item(Guid.NewGuid());
            theCache.Store(item.Id, item);
            items.Add(item);
        }

        theCache.CompactIfNecessary();

        // The first 10 should have removed
        for (int i = 0; i < 10; i++)
        {
            theCache.TryFind(items[0].Id, out var _).ShouldBeFalse();
        }

        theCache.Count.ShouldBe(theCache.Limit);
    }

    [Fact]
    public void request_item_resets()
    {
        var items = new List<Item>();
        for (int i = 0; i < 110; i++)
        {
            var item = new Item(Guid.NewGuid());
            theCache.Store(item.Id, item);
            items.Add(item);
        }

        theCache.TryFind(items[0].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[2].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[4].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[8].Id, out var _).ShouldBeTrue();

        theCache.CompactIfNecessary();
        theCache.Count.ShouldBe(theCache.Limit);

        theCache.TryFind(items[0].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[2].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[4].Id, out var _).ShouldBeTrue();
        theCache.TryFind(items[8].Id, out var _).ShouldBeTrue();

        theCache.TryFind(items[1].Id, out var _).ShouldBeFalse();
        theCache.TryFind(items[3].Id, out var _).ShouldBeFalse();
        theCache.TryFind(items[5].Id, out var _).ShouldBeFalse();
        theCache.TryFind(items[7].Id, out var _).ShouldBeFalse();

    }
}

public record Item(Guid Id);
