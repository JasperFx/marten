using System;
using Baseline;
using Marten.Schema;
using Marten.Testing.Schema.Identity.Sequences;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_foreign_key_fields_Tests
    {
        [Fact]
        public void should_get_foreign_key_from_attribute()
        {
            TestingDocumentStore.Basic().Storage.MappingFor(typeof(Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "user_id");
        }

        [Fact]
        public void should_get_foreign_key_from_registry()
        {
            var storeOptions = new StoreOptions();
            storeOptions.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);

            var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);
            });

            store.Storage.MappingFor(typeof(Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "other_user_id");
        }

        [Fact]
        public void should_allow_self_reference()
        {
            TestingDocumentStore.Basic().Storage.MappingFor(typeof(Employee))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "manager_id");
        }

        [Fact]
        public void should_allow_foreign_key_on_id_field()
        {
            var storeOptions = new StoreOptions();


            var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<Foo>()
                    .Identity(x => x.FooId);
                _.Schema.For<FooExtra>()
                    .Identity(x => x.FooId)
                    .ForeignKey<Foo>(x => x.FooId);
            });

            store.Storage.MappingFor(typeof(FooExtra))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "foo_id");
        }

        // SAMPLE: issue-with-fk-attribute
        public class Issue
        {
            public Issue()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            [ForeignKey(typeof(User))]
            public Guid UserId { get; set; }

            public Guid? OtherUserId { get; set; }
        }

        // ENDSAMPLE

        public class User
        {
            public User()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            public string Name { get; set; }
        }

        public class Employee
        {
            public Employee()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }

            [ForeignKey(typeof(Employee))]
            public Guid? ManagerId { get; set; }
        }

        public class Foo
        {
            public Guid FooId { get; set; }
        }
        public class FooExtra
        {
            public Guid FooId { get; set; }
        }
    }
}
