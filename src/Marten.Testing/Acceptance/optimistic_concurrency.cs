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

        [Fact]
        public void can_do_multiple_updates_in_a_row_standard()
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

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc2 = session.Load<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                session.SaveChanges();

                doc2.Name = "Cafe Medici";

                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Cafe Medici");
            }
        }


        [Fact]
        public async Task can_do_multiple_updates_in_a_row_standard_async()
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

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                await session.SaveChangesAsync();

                doc2.Name = "Cafe Medici";

                await session.SaveChangesAsync();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Cafe Medici");
            }
        }

        [Fact]
        public void can_do_multiple_updates_in_a_row_legacy()
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

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc2 = session.Load<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                session.SaveChanges();

                doc2.Name = "Cafe Medici";

                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Cafe Medici");
            }
        }


        [Fact]
        public async Task can_do_multiple_updates_in_a_row_legacy_async()
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

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc2.Name = "Mozart's";

                await session.SaveChangesAsync();

                doc2.Name = "Cafe Medici";

                await session.SaveChangesAsync();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Cafe Medici");
            }
        }

        [Fact]
        public void update_multiple_docs_at_a_time_happy_path()
        {
            var doc1 = new CoffeeShop();
            var doc2 = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2);
                session.SaveChanges();
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = session.Load<CoffeeShop>(doc1.Id);
                doc12.Name = "Mozart's";

                var doc22 = session.Load<CoffeeShop>(doc2.Id);
                doc22.Name = "Dominican Joe's";

                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Mozart's");
                query.Load<CoffeeShop>(doc2.Id).Name.ShouldBe("Dominican Joe's");
            }
        }

        [Fact]
        public async Task update_multiple_docs_at_a_time_happy_path_async()
        {
            var doc1 = new CoffeeShop();
            var doc2 = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc12.Name = "Mozart's";

                var doc22 = await session.LoadAsync<CoffeeShop>(doc2.Id);
                doc22.Name = "Dominican Joe's";

                await session.SaveChangesAsync();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id)).Name.ShouldBe("Mozart's");
                (await query.LoadAsync<CoffeeShop>(doc2.Id)).Name.ShouldBe("Dominican Joe's");
            }
        }

        [Fact]
        public void update_multiple_docs_at_a_time_sad_path()
        {
            var doc1 = new CoffeeShop();
            var doc2 = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2);
                session.SaveChanges();
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = session.Load<CoffeeShop>(doc1.Id);
                doc12.Name = "Mozart's";

                var doc22 = session.Load<CoffeeShop>(doc2.Id);
                doc22.Name = "Dominican Joe's";


                using (var other = theStore.DirtyTrackedSession())
                {
                    other.Load<CoffeeShop>(doc1.Id).Name = "Genuine Joe's";
                    other.Load<CoffeeShop>(doc2.Id).Name = "Cafe Medici";

                    other.SaveChanges();
                }

                var ex = Exception<AggregateException>.ShouldBeThrownBy(() =>
                {
                    session.SaveChanges();
                });

                ex.InnerExceptions.OfType<ConcurrencyException>().Count().ShouldBe(2);
            }
        }



        [Fact]
        public async Task update_multiple_docs_at_a_time_sad_path_async()
        {
            var doc1 = new CoffeeShop();
            var doc2 = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(doc1, doc2);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = await session.LoadAsync<CoffeeShop>(doc1.Id);
                doc12.Name = "Mozart's";

                var doc22 = await session.LoadAsync<CoffeeShop>(doc2.Id);
                doc22.Name = "Dominican Joe's";


                using (var other = theStore.DirtyTrackedSession())
                {
                    (await other.LoadAsync<CoffeeShop>(doc1.Id)).Name = "Genuine Joe's";
                    (await other.LoadAsync<CoffeeShop>(doc2.Id)).Name = "Cafe Medici";

                    await other.SaveChangesAsync();
                }

                var ex = await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session.SaveChangesAsync();
                });

                ex.InnerExceptions.OfType<ConcurrencyException>().Count().ShouldBe(2);
            }
        }

        [Fact]
        public void store_with_the_right_version()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }

            var metadata = theStore.Advanced.MetadataFor(doc1);

            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";
                session.Store(doc1, metadata.CurrentVersion);

                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<CoffeeShop>(doc1.Id).Name
                    .ShouldBe("Mozart's");
            }
        }

        [Fact]
        public async Task store_with_the_right_version_async()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }

            var metadata = theStore.Advanced.MetadataFor(doc1);

            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";
                session.Store(doc1, metadata.CurrentVersion);

                await session.SaveChangesAsync();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id)).Name
                    .ShouldBe("Mozart's");
            }
        }

        [Fact]
        public void store_with_the_right_version_sad_path()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();
            }


            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";

                // Some random version that won't match
                session.Store(doc1, Guid.NewGuid());

                Exception<AggregateException>.ShouldBeThrownBy(() =>
                {
                    session.SaveChanges();
                });
            }


        }

        [Fact]
        public async Task store_with_the_right_version_sad_path_async()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync();
            }


            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";

                // Some random version that won't match
                session.Store(doc1, Guid.NewGuid());

                await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session.SaveChangesAsync();
                });
            }


        }
    }

    [UseOptimisticConcurrency]
    public class Shop
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    [UseOptimisticConcurrency]
    public class CoffeeShop : Shop
    {
        // Guess where I'm at as I code this?
        public string Name { get; set; } = "Starbucks";
    }
}