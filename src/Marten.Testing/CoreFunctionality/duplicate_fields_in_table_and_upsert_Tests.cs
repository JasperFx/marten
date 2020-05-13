using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    [Collection("duplicated")]
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

            var runner = theStore.Tenancy.Default.OpenConnection();
            runner.QueryScalar<string>($"select first_name from duplicated.mt_doc_user where id = '{user1.Id}'")
                  .ShouldBe("Byron");
        }

        public duplicate_fields_in_table_and_upsert_Tests() : base("duplicated")
        {
        }
    }
}
