using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class optimistic_concurrency : IntegratedFixture
    {
        [Fact]
        public void can_generate_the_upsert_smoke_test_with_94_style()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });

            theStore.Schema.EnsureStorageExists(typeof(Issue));
        }

        [Fact]
        public void can_generate_the_upsert_smoke_test_with_95_style()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });

            theStore.Schema.EnsureStorageExists(typeof(Issue));
        }

        [Fact]
        public void can_insert_with_optimistic_concurrency_94()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

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
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

            using (var session = theStore.OpenSession())
            {
                var coffeeShop = new CoffeeShop();
                session.Store(coffeeShop);
                await session.SaveChangesAsync();

                (await session.LoadAsync<CoffeeShop>(coffeeShop.Id)).ShouldNotBeNull();
            }
        }


        [Fact]
        public void can_insert_with_optimistic_concurrency_95()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

            using (var session = theStore.OpenSession())
            {
                var coffeeShop = new CoffeeShop();
                session.Store(coffeeShop);
                session.SaveChanges();

                session.Load<CoffeeShop>(coffeeShop.Id).ShouldNotBeNull();
            }
        }

        [Fact]
        public async Task can_insert_with_optimistic_concurrency_95_async()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

            using (var session = theStore.OpenSession())
            {
                var coffeeShop = new CoffeeShop();
                session.Store(coffeeShop);
                await session.SaveChangesAsync();

                (await session.LoadAsync<CoffeeShop>(coffeeShop.Id)).ShouldNotBeNull();
            }
        }



        [Fact]
        public void can_update_with_optimistic_concurrency_94()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = session.Load<CoffeeShop>(doc1.Id);
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
        public async Task can_update_with_optimistic_concurrency_94_async()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                session.Store(doc2);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Mozart's");
            }
        }


        [Fact]
        public void can_update_with_optimistic_concurrency_95()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = session.Load<CoffeeShop>(doc1.Id);
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
        public async Task can_update_with_optimistic_concurrency_95_async()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                session.Store(doc2);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Mozart's");
            }
        }

        [Fact]
        public void update_with_stale_version_standard()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

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
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
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
        public void update_with_stale_version_legacy()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

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
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
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
        public async Task update_with_stale_version_standard_async()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }

            var session1 = theStore.DirtyTrackedSession();
            var session2 = theStore.DirtyTrackedSession();

            var session1Copy = await session1.LoadAsync<CoffeeShop>(doc1.Id);
            var session2Copy = await session2.LoadAsync<CoffeeShop>(doc1.Id);

            try
            {
                session1Copy.Name = "Mozart's";
                session2Copy.Name = "Dominican Joe's";

                // Should go through just fine
                await session2.SaveChangesAsync();


                var ex = await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session1.SaveChangesAsync();
                });

                var concurrency = ex.InnerExceptions.OfType<ConcurrencyException>().Single();
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
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



        [Fact]
        public async Task update_with_stale_version_legacy_async()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
            });

            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }

            var session1 = theStore.DirtyTrackedSession();
            var session2 = theStore.DirtyTrackedSession();

            var session1Copy = await session1.LoadAsync<CoffeeShop>(doc1.Id);
            var session2Copy = await session2.LoadAsync<CoffeeShop>(doc1.Id);

            try
            {
                session1Copy.Name = "Mozart's";
                session2Copy.Name = "Dominican Joe's";

                // Should go through just fine
                await session2.SaveChangesAsync();


                var ex = await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session1.SaveChangesAsync();
                });

                var concurrency = ex.InnerExceptions.OfType<ConcurrencyException>().Single();
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
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

    [UseOptimisticConcurrency]
    public class CoffeeShop
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Guess where I'm at as I code this?
        public string Name { get; set; } = "Starbucks";
    }
}