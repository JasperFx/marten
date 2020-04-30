using System;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Schema
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
