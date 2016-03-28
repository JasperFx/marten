using System;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class ProductionModeSchemeCreationTests
    {
        [Fact]
        public void work_with_existing_tables()
        {
            using (var c1 = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = c1.GetInstance<IDocumentStore>().OpenSession())
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

        [Fact]
        public void throw_exception_when_table_does_not_exist()
        {
            using (var c1 = Container.For<DevelopmentModeRegistry>())
            {
                c1.GetInstance<DocumentCleaner>().CompletelyRemoveAll();
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.None;
            }))
            {
                using (var session = store.OpenSession())
                {
                    Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
                    {
                        session.Store(new User());
                        session.SaveChanges();
                    });


                }
            }

            
        }

    }
}