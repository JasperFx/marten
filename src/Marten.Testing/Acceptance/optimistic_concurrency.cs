using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Marten.Testing;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;
using System.Collections.Generic;

namespace Marten.Testing.Acceptance
{
    public class optimistic_concurrency : IntegratedFixture
    {


        public void example_configuration()
        {
            // SAMPLE: configuring-optimistic-concurrency
            var store = DocumentStore.For(_ =>
            {
                // Adds optimistic concurrency checking to Issue
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void can_generate_the_upsert_smoke_test_with_95_style()
        {
            StoreOptions(_ => {
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });

            theStore.Schema.EnsureStorageExists(typeof(Issue));
        }



        [Fact]
        public void can_insert_with_optimistic_concurrency_95()
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
        public async Task can_insert_with_optimistic_concurrency_95_async()
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
        public void can_store_same_document_multiple_times_with_optimistic_concurrency()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.Store(doc1);

                session.SaveChanges();
            }
        }

        [Fact]
        public void can_patch_and_store_document_with_optimistic_concurrency_before_savechanges()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                session.SaveChanges();

                session.Patch<CoffeeShop>(doc1.Id).Set(x => x.Name, "New Name");
                session.Store(doc1);
                session.SaveChanges();
            }
        }

        [Fact]
        public void can_update_with_optimistic_concurrency_95()
        {
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
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.OpenSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);
                doc2.Name = "Mozart's";

                session.Store(doc2);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name.ShouldBe("Mozart's");
            }
        }


        // SAMPLE: update_with_stale_version_standard
        [Fact]
        public void update_with_stale_version_standard()
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
        // ENDSAMPLE


        [Fact]
        public async Task update_with_stale_version_standard_async()
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
                concurrency.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
            }
            finally
            {
                session1.Dispose();
                session2.Dispose();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name.ShouldBe("Dominican Joe's");
            }

        }



        [Fact]
        public void can_do_multiple_updates_in_a_row_standard()
        {
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
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc2 = await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);
                doc2.Name = "Mozart's";

                await session.SaveChangesAsync().ConfigureAwait(false);

                doc2.Name = "Cafe Medici";

                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name.ShouldBe("Cafe Medici");
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
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);
                doc12.Name = "Mozart's";

                var doc22 = await session.LoadAsync<CoffeeShop>(doc2.Id).ConfigureAwait(false);
                doc22.Name = "Dominican Joe's";

                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name.ShouldBe("Mozart's");
                (await query.LoadAsync<CoffeeShop>(doc2.Id).ConfigureAwait(false)).Name.ShouldBe("Dominican Joe's");
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
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.DirtyTrackedSession())
            {
                var doc12 = await session.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false);
                doc12.Name = "Mozart's";

                var doc22 = await session.LoadAsync<CoffeeShop>(doc2.Id).ConfigureAwait(false);
                doc22.Name = "Dominican Joe's";


                using (var other = theStore.DirtyTrackedSession())
                {
                    (await other.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name = "Genuine Joe's";
                    (await other.LoadAsync<CoffeeShop>(doc2.Id).ConfigureAwait(false)).Name = "Cafe Medici";

                    await other.SaveChangesAsync().ConfigureAwait(false);
                }

                var ex = await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session.SaveChangesAsync().ConfigureAwait(false);
                });

                ex.InnerExceptions.OfType<ConcurrencyException>().Count().ShouldBe(2);
            }
        }

        // SAMPLE: store_with_the_right_version
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
        // ENDSAMPLE

        [Fact]
        public async Task store_with_the_right_version_async()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession())
            {
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            var metadata = theStore.Advanced.MetadataFor(doc1);

            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";
                session.Store(doc1, metadata.CurrentVersion);

                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var query = theStore.QuerySession())
            {
                (await query.LoadAsync<CoffeeShop>(doc1.Id).ConfigureAwait(false)).Name
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
                await session.SaveChangesAsync().ConfigureAwait(false);
            }


            using (var session = theStore.OpenSession())
            {
                doc1.Name = "Mozart's";

                // Some random version that won't match
                session.Store(doc1, Guid.NewGuid());

                await Exception<AggregateException>.ShouldBeThrownByAsync(async () =>
                {
                    await session.SaveChangesAsync().ConfigureAwait(false);
                });
            }


        }


        [Fact]
        public async Task can_update_and_delete_related_documents()
        {
            var emp1 = new CoffeeShopEmployee();
            var doc1 = new CoffeeShop();
            doc1.Employees.Add(emp1.Id);

            using (var session = theStore.OpenSession())
            {
                session.Store(emp1);
                session.Store(doc1);
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var session = theStore.OpenSession(tracking: DocumentTracking.DirtyTracking))
            {
                var emp = session.Load<CoffeeShopEmployee>(emp1.Id);
                var doc = session.Load<CoffeeShop>(doc1.Id);

                
                doc.Employees.Remove(emp.Id);
                session.Delete(emp);

                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        [Fact]
        public void can_update_and_delete_related_documents_synchronous()
        {
            var emp1 = new CoffeeShopEmployee();
            var doc1 = new CoffeeShop();
            doc1.Employees.Add(emp1.Id);

            using (var session = theStore.OpenSession())
            {
                session.Store(emp1);
                session.Store(doc1);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession(tracking: DocumentTracking.DirtyTracking))
            {
                var emp = session.Load<CoffeeShopEmployee>(emp1.Id);
                var doc = session.Load<CoffeeShop>(doc1.Id);


                doc.Employees.Remove(emp.Id);
                session.Delete(emp);

                session.SaveChanges();
            }
        }

        [Fact]
        public void Bug_669_can_store_and_update_same_document_with_optimistic_concurrency_and_dirty_tracking()
        {
            var doc1 = new CoffeeShop();
            using (var session = theStore.OpenSession(tracking: DocumentTracking.DirtyTracking))
            {
                session.Store(doc1);
                doc1.Name = "New Name";
                session.SaveChanges();
            }
        }
    }

    [UseOptimisticConcurrency]
    public class Shop
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    // SAMPLE: UseOptimisticConcurrencyAttribute
    [UseOptimisticConcurrency]
    public class CoffeeShop : Shop
    {
        // Guess where I'm at as I code this?
        public string Name { get; set; } = "Starbucks";
        public ICollection<Guid> Employees { get; set; } = new List<Guid>();
    }
    // ENDSAMPLE

    [SoftDeleted]
    [UseOptimisticConcurrency]
    public class CoffeeShopEmployee
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
    }

}