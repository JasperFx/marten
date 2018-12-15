using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class duplicate_fields_in_table_and_upsert_Tests : IntegratedFixture
    {
        [Fact]
        public void end_to_end()
        {
            theStore.Storage.MappingFor(typeof(User)).As<DocumentMapping>().DuplicateField("FirstName");
            
            var user1 = new User { FirstName = "Byron", LastName = "Scott" };
            using (var session = theStore.OpenSession())
            {
                session.Store(user1);
                session.SaveChanges();
            }

            var runner = theStore.Tenancy.Default.OpenConnection();
            runner.QueryScalar<string>($"select first_name from mt_doc_user where id = '{user1.Id}'")
                  .ShouldBe("Byron");
        } 
    }
}