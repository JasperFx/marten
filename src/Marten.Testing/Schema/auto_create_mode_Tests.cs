using System;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class auto_create_mode_Tests
    {
        [Fact]
        public void cannot_add_fields_if_mode_is_create_only()
        {
            var user1 = new User { FirstName = "Jeremy" };
            var user2 = new User { FirstName = "Max" };
            var user3 = new User { FirstName = "Declan" };

            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                store.Advanced.Clean.CompletelyRemoveAll();

                store.BulkInsert(new User[] { user1, user2, user3 });
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Searchable(x => x.FirstName);
            }))
            {
                var ex = Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
                {
                    store2.Schema.EnsureStorageExists(typeof(User));
                });

                ex.Message.ShouldBe($"The table for document type {typeof(User).FullName} is different than the current schema table, but AutoCreateSchemaObjects = '{nameof(AutoCreate.CreateOnly)}'");
            }
        }
    }


}