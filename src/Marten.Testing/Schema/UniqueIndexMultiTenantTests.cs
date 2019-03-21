using System;
using Marten.Schema;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class UniqueIndexMultiTenantTests
    {
        public const string UniqueSqlState = "23505";

        private class Project
        {
            public Project()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            public string Name { get; set; }
        }


        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_single_property_for_different_tenants_succeeds()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<Project>().MultiTenanted();
                _.Schema.For<Project>().UniqueIndex(UniqueIndexType.Tenanted, x => x.Name);
                _.Connection(ConnectionSource.ConnectionString);

            });

            //default tenant unique constraints still work
            using (var session = store.OpenSession())
            {
                session.Store(new Project { Name = "Project A" });
                session.SaveChanges();

                session.Store(new Project { Name = "Project A" });

                try
                {
                    session.SaveChanges();
                }
                catch (MartenCommandException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                }
            }

            //but tenant abc can add a project with the same name
            using (var session = store.OpenSession("abc"))
            {
                session.Store(new Project { Name = "Project A" });
                session.SaveChanges();
            }

            //as can tenant def, but only once within the tenant
            using (var session = store.OpenSession("def"))
            {
                session.Store(new Project { Name = "Project A" });
                session.SaveChanges();
                session.Store(new Project { Name = "Project A" });

                try
                {
                    session.SaveChanges();
                }
                catch (MartenCommandException exception)
                {
                    ((PostgresException)exception.InnerException).SqlState.ShouldBe(UniqueSqlState);
                }
            }
        }
    }
}