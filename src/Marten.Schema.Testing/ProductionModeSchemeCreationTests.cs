using System.Linq;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class ProductionModeSchemeCreationTests
    {
        [Fact]
        public void work_with_existing_tables()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
            }))
            {
                store.Advanced.Clean.CompletelyRemoveAll();

                using (var session = store.OpenSession())
                {
                    session.Store(new User());
                    session.Store(new User());
                    session.SaveChanges();
                }
            }


            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.None;
            }))
            {
                using (var session = store.OpenSession())
                {
                    session.Query<User>().Count().ShouldBeGreaterThan(0);
                }
            }
        }


    }
}
