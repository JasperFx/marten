using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class duplicate_fields_in_table_and_upsert_Tests : OneOffConfigurationsContext
    {
        [Fact]
        public void end_to_end()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<User>().Duplicate(x => x.FirstName);
            });

            var user1 = new User { FirstName = "Byron", LastName = "Scott" };
            using (var session = theStore.OpenSession())
            {
                session.Store(user1);
                session.SaveChanges();
            }

            using var conn = theStore.Tenancy.Default.Database.CreateConnection();
            conn.Open();
            conn.CreateCommand($"select first_name from duplicated.mt_doc_user where id = '{user1.Id}'")
                .ExecuteScalar().As<string>().ShouldBe("Byron");

        }

    }
}
