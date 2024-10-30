using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Metadata;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class OptimisticConcurrencyWithUpdateMethodStoreFixture: StoreFixture
{
    public OptimisticConcurrencyWithUpdateMethodStoreFixture(): base("optimistic_concurrency_with_update_method")
    {
    }
}

public class optimistic_concurrency_with_update_method: StoreContext<OptimisticConcurrencyWithUpdateMethodStoreFixture>,
    IClassFixture<OptimisticConcurrencyWithUpdateMethodStoreFixture>
{
    public optimistic_concurrency_with_update_method(OptimisticConcurrencyWithUpdateMethodStoreFixture fixture):
        base(fixture)
    {
    }

    [Fact]
    public async Task can_update_with_optimistic_concurrency()
    {
        var doc1 = new CoffeeShop();
        using (var session1 = theStore.LightweightSession())
        {
            session1.Insert(doc1);
            await session1.SaveChangesAsync();
        }

        using (var session2 = theStore.LightweightSession())
        {
            var doc2 = await session2.LoadAsync<CoffeeShop>(doc1.Id);
            doc2.Name = "Mozart's";

            session2.Update(doc2);
            await session2.SaveChangesAsync();
        }

        using (var session3 = theStore.QuerySession())
        {
            (await session3.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Mozart's");
        }
    }

    [Fact]
    public async Task can_update_with_optimistic_concurrency_async()
    {
        var doc1 = new CoffeeShop();
        await using (var session1 = theStore.OpenSession())
        {
            session1.Insert(doc1);
            await session1.SaveChangesAsync();
        }

        await using (var session2 = theStore.OpenSession())
        {
            var doc2 = await session2.LoadAsync<CoffeeShop>(doc1.Id);
            doc2.Name = "Mozart's";

            session2.Update(doc2);
            await session2.SaveChangesAsync();
        }

        await using (var session3 = theStore.QuerySession())
        {
            (await session3.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Mozart's");
        }
    }

    [Fact]
    public async Task update_with_stale_version_throws_exception()
    {
        var doc1 = new CoffeeShop();
        using (var session1 = theStore.LightweightSession())
        {
            session1.Insert(doc1);
            await session1.SaveChangesAsync();
        }

        using (var session2 = theStore.LightweightSession())
        {
            var doc2 = await session2.LoadAsync<CoffeeShop>(doc1.Id);
            doc2.Name = "Mozart's";

            // Some random version that won't match
            doc2.Version = Guid.NewGuid();

            session2.Update(doc2);

            var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
            {
                await session2.SaveChangesAsync();
            });

            ex.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
        }
    }

    [Fact]
    public async Task update_with_stale_version_throws_exception_async()
    {
        var doc1 = new CoffeeShop();
        await using (var session1 = theStore.LightweightSession())
        {
            session1.Insert(doc1);
            await session1.SaveChangesAsync();
        }

        await using (var session2 = theStore.LightweightSession())
        {
            var doc2 = await session2.LoadAsync<CoffeeShop>(doc1.Id);
            doc2.Name = "Mozart's";

            // Some random version that won't match
            doc2.Version = Guid.NewGuid();

            session2.Update(doc2);

            var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
            {
                await session2.SaveChangesAsync();
            });

            ex.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
        }
    }

    [Fact]
    public async Task update_with_stale_version_and_disabled_checks()
    {
        var doc1 = new CoffeeShop();
        await using (var session1 = theStore.LightweightSession())
        {
            session1.Insert(doc1);
            await session1.SaveChangesAsync();
        }

        await using (var session2 = theStore.OpenSession(new SessionOptions{ConcurrencyChecks = ConcurrencyChecks.Disabled}))
        {
            var doc2 = await session2.LoadAsync<CoffeeShop>(doc1.Id);
            doc2.Name = "Mozart's";

            // Some random version that won't match
            doc2.Version = Guid.NewGuid();

            session2.Update(doc2);

            await session2.SaveChangesAsync();
        }
    }
}

public class CoffeeShop: IVersioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Starbucks";
    public Guid Version { get; set; }
}
