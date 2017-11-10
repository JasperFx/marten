using System.Linq;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing
{
    public class ability_to_use_an_existing_connection_and_transaction : IntegratedFixture
    {
        readonly Target[] targets = Target.GenerateRandomData(100).ToArray();

        public ability_to_use_an_existing_connection_and_transaction(ITestOutputHelper output = null) : base(output)
        {
            theStore.BulkInsertDocuments(targets);
        }

        [Fact]
        public void pass_in_current_connection_and_transaction()
        {
            var newTargets = Target.GenerateRandomData(5).ToArray();

            using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                conn.Open();
                var tx = conn.BeginTransaction();


                var cmd = conn.CreateCommand("delete from mt_doc_target");
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();


                // To prove the isolation here
                using (var query = theStore.QuerySession())
                {
                    query.Query<Target>().Count().ShouldBe(100);
                }


                using (var session = theStore.OpenSession(new SessionOptions
                {
                    Transaction = tx
                }))
                {
                    session.Store(newTargets);
                    session.SaveChanges();
                }
            }

            // All the old should be gone, then the new put back on top
            using (var query = theStore.QuerySession())
            {
                query.Query<Target>().Count().ShouldBe(5);
            }
        }
    }
}