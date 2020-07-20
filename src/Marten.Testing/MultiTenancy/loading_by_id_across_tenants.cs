using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class loading_by_id_across_tenants : IntegrationContext
    {
        private readonly Target targetRed1 = Target.Random();
        private readonly Target targetRed2 = Target.Random();
        private readonly Target targetBlue1 = Target.Random();
        private readonly Target targetBlue2 = Target.Random();

        public loading_by_id_across_tenants(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();

            });

            using (var session = theStore.OpenSession("Red"))
            {
                session.Store(targetRed1, targetRed2);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Blue"))
            {
                session.Store(targetBlue1, targetBlue2);
                session.SaveChanges();
            }
        }

        [Fact]
        public void cannot_load_by_id_across_tenants()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                SpecificationExtensions.ShouldNotBeNull(red.Load<Target>(targetRed1.Id));
                SpecificationExtensions.ShouldBeNull(red.Load<Target>(targetBlue1.Id));
            }

            using (var blue = theStore.QuerySession("Blue"))
            {
                SpecificationExtensions.ShouldNotBeNull(blue.Load<Target>(targetBlue1.Id));
                SpecificationExtensions.ShouldBeNull(blue.Load<Target>(targetRed1.Id));
            }
        }

        [Fact]
        public void cannot_load_json_by_id_across_tenants()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                red.Json.FindById<Target>(targetRed1.Id).ShouldNotBeNull();
                red.Json.FindById<Target>(targetBlue1.Id).ShouldBeNull();
            }

            using (var blue = theStore.QuerySession("Blue"))
            {
                blue.Json.FindById<Target>(targetBlue1.Id).ShouldNotBeNull();
                blue.Json.FindById<Target>(targetRed1.Id).ShouldBeNull();
            }
        }


        [Fact]
        public async Task cannot_load_json_by_id_across_tenants_async()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                (await red.Json.FindByIdAsync<Target>(targetRed1.Id)).ShouldNotBeNull();
                (await red.Json.FindByIdAsync<Target>(targetBlue1.Id)).ShouldBeNull();
            }

            using (var blue = theStore.QuerySession("Blue"))
            {
                (await blue.Json.FindByIdAsync<Target>(targetBlue1.Id)).ShouldNotBeNull();
                (await blue.Json.FindByIdAsync<Target>(targetRed1.Id)).ShouldBeNull();
            }
        }


        [Fact]
        public async Task cannot_load_by_id_across_tenants_async()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                SpecificationExtensions.ShouldNotBeNull((await red.LoadAsync<Target>(targetRed1.Id)));
                SpecificationExtensions.ShouldBeNull((await red.LoadAsync<Target>(targetBlue1.Id)));
            }

            using (var blue = theStore.QuerySession("Blue"))
            {
                SpecificationExtensions.ShouldNotBeNull((await blue.LoadAsync<Target>(targetBlue1.Id)));
                SpecificationExtensions.ShouldBeNull((await blue.LoadAsync<Target>(targetRed1.Id)));
            }
        }

        [Fact]
        public void cannot_load_by_many_id_across_tenants()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                red.LoadMany<Target>(targetRed1.Id, targetRed2.Id).Count.ShouldBe(2);
                red.LoadMany<Target>(targetBlue1.Id, targetBlue1.Id, targetRed1.Id)
                    .Single()
                    .Id.ShouldBe(targetRed1.Id);
            }

        }


        [Fact]
        public async Task cannot_load_by_many_id_across_tenants_async()
        {
            using (var red = theStore.QuerySession("Red"))
            {
                (await red.LoadManyAsync<Target>(targetRed1.Id, targetRed2.Id)).Count.ShouldBe(2);
                (await red.LoadManyAsync<Target>(targetBlue1.Id, targetBlue1.Id, targetRed1.Id))
                   .Single()
                   .Id.ShouldBe(targetRed1.Id);
            }

        }

    }
}
