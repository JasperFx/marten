using System;
using Baseline;
using Marten.Schema;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_foreign_key_fields_Tests
    {
        [Fact]
        public void should_get_foreign_key_from_attribute()
        {
            var schema = new DocumentSchema(new StoreOptions(), null, null);
            schema.MappingFor(typeof (Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "user_id");
        }

        [Fact]
        public void should_get_foreign_key_from_registry()
        {
            var storeOptions = new StoreOptions();
            storeOptions.Schema.For<Issue>().ForeignKey<User>(i => i.OtherUserId);

            var schema = new DocumentSchema(storeOptions, null, null);

            schema.MappingFor(typeof(Issue))
                .As<DocumentMapping>()
                .ForeignKeys
                .ShouldContain(x => x.ColumnName == "other_user_id");
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
    }
}