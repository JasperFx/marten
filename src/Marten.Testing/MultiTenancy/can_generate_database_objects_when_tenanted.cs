using System.Linq;
using Marten.Testing.Documents;
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