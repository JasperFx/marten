using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class optimistic_concurrency_with_subclass_hierarchies: IntegratedFixture
    {
        public optimistic_concurrency_with_subclass_hierarchies()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Shop>().AddSubClass<CoffeeShop>();
            });
        }

        [Fact]
        public void can_insert_with_optimistic_concurrency()
        {
            using (var session = theStore.OpenSession())
            {
                var coffeeShop = new CoffeeShop();
                session.Store(coffeeShop);
                session.SaveChanges();

                session.Load<CoffeeShop>(coffeeShop.Id).ShouldNotBeNull();
            }
        }

        [Fact]
        public async Task can_insert_with_optimistic_concurrency_94_async()
        {
            using (var session = theStore.OpenSession())
            {
                var coffeeShop = new CoffeeShop();
                session.Store(coffeeShop);
                await session.SaveChangesAsync().ConfigureAwait(false);

                (await session.LoadAsync<CoffeeShop>(coffeeShop.Id).ConfigureAwait(false)).ShouldNotBeNull();
            }
        }

        [Fact]
        public void can_update_with_optimistic_concurrency()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = session.Load<Shop>(doc1.Id).As<CoffeeShop>();
                doc2.Name = "Mozart's";

                session.Store(doc2);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Mozart's");
            }
        }

        [Fact]
        public async Task can_update_with_optimistic_concurrenc_async()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = await session.LoadAsync<Shop>(doc1.Id).ConfigureAwait(false);
                doc2.As<CoffeeShop>().Name = "Mozart's";

                session.Store(doc2);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name.ShouldBe("Mozart's");
            }
        }

        [Fact]
        public void update_with_stale_version()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }

            var session1 = theStore.DirtyTrackedSession();
            var session2 = theStore.DirtyTrackedSession();

            var session1Copy = session1.Load<CoffeeShop>(doc1.Id);
            var session2Copy = session2.Load<CoffeeShop>(doc1.Id);

            try
            {
                session1Copy.Name = "Mozart's";
                session2Copy.Name = "Dominican Joe's";

                // Should go through just fine
                session2.SaveChanges();

                var ex = Exception<AggregateException>.ShouldBeThrownBy(() =>
                {
                    session1.SaveChanges();
                });

                var concurrency = ex.InnerExceptions.OfType<ConcurrencyException>().Single();
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(Shop).FullName} #{doc1.Id}");
            }
            finally
            {
                session1.Dispose();
                session2.Dispose();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Dominican Joe's");
            }
        }

        [Fact]
        public async Task update_with_stale_version_async()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            var session1 = theStore.DirtyTrackedSession();
            var session2 = theStore.DirtyTrackedSession();

            var session1Copy = await session1.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);
            var session2Copy = await session2.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);

            try
            {
                session1Copy.Name = "Mozart's";
                session2Copy.Name = "Dominican Joe's";

                // Should go through just fine
                await session2.SaveChangesAsync().ConfigureAwait(false);

                var ex = await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session1.SaveChangesAsync().ConfigureAwait(false);
                });

                var concurrency = ex.InnerExceptions.OfType<ConcurrencyException>().Single();
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(Shop).FullName} #{doc1.Id}");
            }
            finally
            {
                session1.Dispose();
                session2.Dispose();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Dominican Joe's");
            }
        }
    }
}
