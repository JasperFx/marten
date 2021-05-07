using System;
using Baseline;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class configuring_foreign_key_fields_Tests : IntegrationContext
    {

        [Fact]
        public void should_get_foreign_key_from_attribute()
        {
            theStore.Storage.MappingFor(typeof(Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnNames[0] == "user_id");
        }

        [Fact]
        public void should_get_foreign_key_from_registry()
        {
            var storeOptions = new StoreOptions();
            storeOptions.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);

            var store = StoreOptions(_ =>
            {
                _.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);
            });

            store.Storage.MappingFor(typeof(Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnNames[0] == "other_user_id");
        }

        [Fact]
        public void should_allow_self_reference()
        {
            theStore.Storage.MappingFor(typeof(Employee))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnNames[0] == "manager_id");
        }

        [Fact]
        public void should_allow_foreign_key_on_id_field()
        {

            var store = StoreOptions(_ =>
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
                .ShouldContain(x => x.ColumnNames[0] == "foo_id");
        }

        #region sample_issue-with-fk-attribute
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

        #endregion sample_issue-with-fk-attribute


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
