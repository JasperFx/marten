using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_590_add_FK_later : BugIntegrationContext
    {
        [Fact]
        public void should_add_a_new_FK_to_the_database()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(UserHolder));

            var store = SeparateStore(_ =>
            {
                _.Schema.For<UserHolder>().ForeignKey<User>(x => x.UserId);
            });

            store.Tenancy.Default.EnsureStorageExists(typeof(UserHolder));

            store.Tenancy.Default.DbObjects
                .AllForeignKeys()
                .Any(x => x.Name == "mt_doc_userholder_user_id_fkey")
                .ShouldBeTrue();
        }
    }

    public class UserHolder
    {
        public Guid Id;

        public Guid UserId { get; set; }
    }
}
