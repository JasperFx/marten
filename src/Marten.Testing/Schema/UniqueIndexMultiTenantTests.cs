using System;
using Marten.Schema;
using Marten.Storage;
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

        private class Project2 : Project { } //used for duplicatedfield index tests

        //used for attributes index tests
        private class Project3 : Project
        {
            [UniqueIndex(IsScopedPerTenant = true, IndexType = UniqueIndexType.Computed)]
            public string Code { get; set; }
        } 

        //private class BadProject : Project { } //used for bad index tests

        //[Fact]
        //public void given_document_is_not_tenanted_do_not_allow_tenanted_indexes()
        //{
        //    var e = Assert.Throws<InvalidOperationException>(() =>
        //    {
        //        DocumentStore.For(_ =>
        //            {
        //                //note that BadProject is not configured for multi-tenancy
        //                _.Schema.For<BadProject>().UniqueIndex(UniqueIndexType.Computed, "bad_index_name", true, x => x.Name);
        //            });
        //    });

        //    e.Message.ShouldContain("BadProject is not configured for Conjoined Tenancy");
        //}

        [Fact]
        public void given_two_documents_for_different_tenants_succeeds_using_attribute()
        {
            var store = DocumentStore.For(_ =>
            {
                //index definition set on attribute of Project3 
                _.Schema.For<Project3>().MultiTenanted();
                
                _.Connection(ConnectionSource.ConnectionString);

            });

            using (var session = store.OpenSession())
            {
                session.Store(new Project3 { Code = "ABC" });
                session.SaveChanges();

                session.Store(new Project3 { Code = "ABC" });

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
                _.Schema.For<Project>().UniqueIndex(UniqueIndexType.Computed, "index_name",true,x => x.Name); //have to pass in index name
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
                _.Schema.For<Project2>().MultiTenanted();
                _.Schema.For<Project2>().UniqueIndex(UniqueIndexType.DuplicatedField, "ix_duplicated_field_name", true, x => x.Name); //have to pass in index name
                _.Connection(ConnectionSource.ConnectionString);

            });

            //default tenant unique constraints still work
            using (var session = store.OpenSession())
            {
                session.Store(new Project2 { Name = "Project A" });
                session.SaveChanges();

                session.Store(new Project2 { Name = "Project A" });

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
                session.Store(new Project2 { Name = "Project A" });
                session.SaveChanges();
            }

            //as can tenant def, but only once within the tenant
            using (var session = store.OpenSession("def"))
            {
                session.Store(new Project2 { Name = "Project A" });
                session.SaveChanges();
                session.Store(new Project2 { Name = "Project A" });

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