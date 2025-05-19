using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace PatchingTests.Patching;

public class Bug_2460_parallel_patching: BugIntegrationContext
{
    private const int itemsCount = 50;
    private const int patchedNumber = 1337;

    [Fact(Skip = "Fix in https://github.com/JasperFx/marten/pull/2468")]
    public async Task can_support_parallel_processing()
    {
        using var store = SeparateStore(_ =>
            {
                _.Schema.For<ItemForPatching>();
                _.AutoCreateSchemaObjects = AutoCreate.None;
            }
        );
        await store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        // Delete old items
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(ItemForPatching));

        var items = new List<ItemForPatching>();

        // Seed items
        await using (var session = store.LightweightSession())
        {
            // Create new items
            for (var i = 0; i < itemsCount; i++)
            {
                var id = Guid.NewGuid();
                var item = new ItemForPatching { Id = id, Number = i };
                items.Add(item);
                session.Store(item);
            }

            await session.SaveChangesAsync();
        }

        // Check count
        await using (var querySession = store.QuerySession())
        {
            var count = await querySession.Query<ItemForPatching>().CountAsync();
            count.ShouldBe(itemsCount);
        }

        // Patch items concurrently
        await Task.WhenAll(items.Select(item => PatchItemAsync(store, item.Id)));

        // Check count after update
        await using (var querySession = store.QuerySession())
        {
            var count = await querySession.Query<ItemForPatching>().Where(x => x.Number == patchedNumber).CountAsync();
            count.ShouldBe(itemsCount);
        }
    }

    private static async Task PatchItemAsync(IDocumentStore store, Guid itemId)
    {
        await Task.Delay(100);
        await using var session = store.LightweightSession();
        session.Patch<ItemForPatching>(itemId).Set(x => x.Number, patchedNumber);
        await session.SaveChangesAsync();
    }
}

public class ItemForPatching
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}
