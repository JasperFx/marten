using System;
using System.Linq;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    [Collection("multi_tenancy")]
    public class can_generate_database_objects_when_tenanted : OneOffConfigurationsContext
    {
        public can_generate_database_objects_when_tenanted() : base("multi_tenancy")
        {
        }

        [Fact]
        public void do_not_blow_up()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
            });


            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));
        }

        [Fact]
        public void can_add_same_primary_key_to_multiple_tenant()
        {
            var guid = Guid.NewGuid();
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Logger(new ConsoleMartenLogger());
            });

            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));
            var existing = theStore.Tenancy.Default.DbObjects.ExistingTableFor(typeof(Target));
            var mapping = theStore.Options.Storage.MappingFor(typeof(Target));
            var expected = new DocumentTable(mapping);

            var delta = new TableDelta(expected, existing);
            delta.Matches.ShouldBeTrue();

            using (var session = theStore.OpenSession("123"))
            {
                var target = Target.Random();
                target.Id = guid;
                target.String = "123";
                session.Store("123", target);
                session.SaveChanges();
            }
            using (var session = theStore.OpenSession("abc"))
            {
                var target = Target.Random();
                target.Id = guid;
                target.String = "abc";
                session.Store("abc", target);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("123"))
            {
                var target = session.Load<Target>(guid);
                SpecificationExtensions.ShouldNotBeNull(target);
                target.String.ShouldBe("123");
            }

            using (var session = theStore.OpenSession("abc"))
            {
                var target = session.Load<Target>(guid);
                SpecificationExtensions.ShouldNotBeNull(target);
                target.String.ShouldBe("abc");
            }
        }

        [Fact]
        public void can_upsert_in_multi_tenancy()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            using (var session = theStore.OpenSession("123"))
            {
                session.Store(Target.GenerateRandomData(10).ToArray());
                session.SaveChanges();
            }
        }

        [Fact]
        public void can_bulk_insert_with_multi_tenancy_on()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            theStore.Advanced.Clean.CompletelyRemoveAll();

            theStore.BulkInsert("345",Target.GenerateRandomData(100).ToArray());
        }
    }
}
