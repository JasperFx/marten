using System;
using Marten.Schema;
using Marten.Schema.Indexing.Unique;
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

        private class ProjectUsingDuplicateField : Project { } //used for duplicatedfield index tests

        //used for attributes index tests
        private class UniqueCodePerTenant
        {
            public UniqueCodePerTenant()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            [UniqueIndex(TenancyScope = TenancyScope.PerTenant, IndexType = UniqueIndexType.Computed, IndexName = "ScopedPerTenant")]
            public string Code { get; set; }
        }

        [Fact]
        public void given_two_documents_for_different_tenants_succeeds_using_attribute()
        {
            var store = DocumentStore.For(_ =>
            {
                //index definition set on attribute of UniqueCodePerTenant
                _.Schema.For<UniqueCodePerTenant>().MultiTenanted();

                _.Connection(ConnectionSource.ConnectionString);
                _.NameDataLength = 100;
            });

            using (var session = store.OpenSession())
            {
                session.Store(new UniqueCodePerTenant { Code = "ABC" });
                session.SaveChanges();

                session.Store(new UniqueCodePerTenant { Code = "ABC" });

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

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_single_property_for_different_tenants_succeeds_using_computed_index()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<Project>().MultiTenanted();
                _.Schema.For<Project>().UniqueIndex(UniqueIndexType.Computed, "index_name", TenancyScope.PerTenant, x => x.Name); //have to pass in index name
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

        [Fact]
        public void given_two_documents_with_the_same_value_for_unique_field_with_single_property_for_different_tenants_succeeds_using_duplicated_field()
        {
            var store = DocumentStore.For(_ =>
            {
                _.NameDataLength = 200;
                _.Schema.For<ProjectUsingDuplicateField>().MultiTenanted();
                _.Schema.For<ProjectUsingDuplicateField>().DocumentAlias("ProjectUsingDuplicateField");
                _.Schema.For<ProjectUsingDuplicateField>().UniqueIndex(UniqueIndexType.DuplicatedField, "ix_duplicate_field", TenancyScope.PerTenant, x => x.Name); //have to pass in index name
                _.Connection(ConnectionSource.ConnectionString);
            });

            //default tenant unique constraints still work
            using (var session = store.OpenSession())
            {
                session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
                session.SaveChanges();

                session.Store(new ProjectUsingDuplicateField { Name = "Project A" });

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
                session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
                session.SaveChanges();
            }

            //as can tenant def, but only once within the tenant
            using (var session = store.OpenSession("def"))
            {
                session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
                session.SaveChanges();
                session.Store(new ProjectUsingDuplicateField { Name = "Project A" });

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