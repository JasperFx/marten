using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
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

    }

    [UseOptimisticConcurrency]
    public class CoffeeShop
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}