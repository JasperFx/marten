using System;
using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_590_add_FK_later
    {
        [Fact]
        public void should_add_a_new_FK_to_the_database()
        {
            using (var store = DocumentStore.For(_ => _.Connection(ConnectionSource.ConnectionString)))
            {
                store.Advanced.Clean.CompletelyRemoveAll();
                store.Tenancy.Default.EnsureStorageExists(typeof(UserHolder));
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<UserHolder>().ForeignKey<User>(x => x.UserId);
            }))
            {
                store.Tenancy.Default.EnsureStorageExists(typeof(UserHolder));

                store.Tenancy.Default.DbObjects
                    .AllForeignKeys()
                    .Any(x => x.Name == "mt_doc_userholder_user_id_fkey")
                    .ShouldBeTrue();
            }
        }
    }

    public class UserHolder
    {
        public Guid Id;

        public Guid UserId { get; set; }
    }
}