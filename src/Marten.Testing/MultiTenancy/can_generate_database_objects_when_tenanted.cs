using System;
using System.Linq;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class can_generate_database_objects_when_tenanted
    {
        [Fact]
        public void do_not_blow_up()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            store.Tenancy.Default.EnsureStorageExists(typeof(User));
        }

        //[Fact] -- unreliable in CI
        public void can_add_same_primary_key_to_multiple_tenant()
        {
            var guid = Guid.NewGuid();
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Logger(new ConsoleMartenLogger());
            });

            store.Tenancy.Default.EnsureStorageExists(typeof(Target));
            var existing = store.Tenancy.Default.DbObjects.ExistingTableFor(typeof(Target));
            var mapping = store.Options.Storage.MappingFor(typeof(Target));
            var expected = new DocumentTable(mapping);

            var delta = new TableDelta(expected, existing);
            delta.Matches.ShouldBeTrue();

            using (var session = store.OpenSession("123"))
            {
                var target = Target.Random();
                target.Id = guid;
                target.String = "123";
                session.Store("123", target);
                session.SaveChanges();
            }
            using (var session = store.OpenSession("abc"))
            {
                var target = Target.Random();
                target.Id = guid;
                target.String = "abc";
                session.Store("abc", target);
                session.SaveChanges();
            }

            using (var session = store.OpenSession("123"))
            {
                var target = session.Load<Target>(guid);
                target.ShouldNotBeNull();
                target.String.ShouldBe("123");
            }

            using (var session = store.OpenSession("abc"))
            {
                var target = session.Load<Target>(guid);
                target.ShouldNotBeNull();
                target.String.ShouldBe("abc");
            }
        }

        [Fact]
        public void can_upsert_in_multi_tenancy()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            using (var session = store.OpenSession("123"))
            {
                session.Store(Target.GenerateRandomData(10).ToArray());
                session.SaveChanges();
            }
        }

        [Fact]
        public void can_bulk_insert_with_multi_tenancy_on()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            store.Advanced.Clean.CompletelyRemoveAll();

            store.BulkInsert("345",Target.GenerateRandomData(100).ToArray());
        }
    }
}