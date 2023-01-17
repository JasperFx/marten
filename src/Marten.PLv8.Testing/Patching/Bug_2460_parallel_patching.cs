using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Patching;

public class Bug_2460_parallel_patching: BugIntegrationContext
{
    private const int itemsCount = 100;
    private const int patchedNumber = 1337;

    [Fact]
    public async Task can_support_parallel_processing()
    {
        using var store = SeparateStore(_ =>
            _.UseJavascriptTransformsAndPatching()
        );

        var items = new List<Item>();

        // Seed items
        await using (var session = store.LightweightSession())
        {
            // Delete old items
            session.DeleteWhere<Item>(_ => true);
            await session.SaveChangesAsync();

            // Create new items
            for (var i = 0; i < itemsCount; i++)
            {
                var id = Guid.NewGuid();
                var item = new Item { Id = id, Number = i };
                items.Add(item);
                session.Store(item);
            }

            await session.SaveChangesAsync();
        }

        // Check count
        await using (var querySession = store.QuerySession())
        {
            var count = await querySession.Query<Item>().CountAsync();
            count.ShouldBe(itemsCount);
        }

        // Patch items concurrently
        await Task.WhenAll(items.Select(item => PatchItemAsync(store, item.Id)));

        // Check count after update
        await using (var querySession = store.QuerySession())
        {
            var count = await querySession.Query<Item>().Where(x => x.Number == patchedNumber).CountAsync();
            count.ShouldBe(itemsCount);
        }
    }

    private static async Task PatchItemAsync(IDocumentStore store, Guid itemId)
    {
        await Task.Delay(100);
        await using var session = store.LightweightSession();
        session.Patch<Item>(itemId).Set(x => x.Number, patchedNumber);
        await session.SaveChangesAsync();
    }

    public class Item
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
    }
}
