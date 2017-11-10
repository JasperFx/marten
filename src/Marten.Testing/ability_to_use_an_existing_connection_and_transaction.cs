using System.Linq;
using System.Threading.Tasks;
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
        public void enlist_in_transaction_scope()
        {
                        
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

        [Fact]
        public async Task pass_in_current_connection_and_transaction_async()
        {
            var newTargets = Target.GenerateRandomData(5).ToArray();

            using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                await conn.OpenAsync();
                var tx = conn.BeginTransaction();


                var cmd = conn.CreateCommand("delete from mt_doc_target");
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync();


                // To prove the isolation here
                using (var query = theStore.QuerySession())
                {
                    (await query.Query<Target>().CountAsync()).ShouldBe(100);
                }


                using (var session = theStore.OpenSession(new SessionOptions
                {
                    Transaction = tx
                }))
                {
                    session.Store(newTargets);
                    await session.SaveChangesAsync();
                }
            }

            // All the old should be gone, then the new put back on top
            using (var query = theStore.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(5);
            }
        }

        [Fact]
        public void pass_in_current_connection_and_transaction_with_externally_controlled_tx_boundaries()
        {
            var newTargets = Target.GenerateRandomData(5).ToArray();

            using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                conn.Open();
                var tx = conn.BeginTransaction();


                var cmd = conn.CreateCommand("delete from mt_doc_target");
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();





                using (var session = theStore.OpenSession(new SessionOptions
                {
                    Transaction = tx,
                    OwnsTransactionLifecycle = false
                }))
                {
                    session.Store(newTargets);
                    session.SaveChanges();
                }

                // To prove the isolation here
                using (var query = theStore.QuerySession())
                {
                    query.Query<Target>().Count().ShouldBe(100);
                }

                tx.Commit();
            }

            // All the old should be gone, then the new put back on top
            using (var query = theStore.QuerySession())
            {
                query.Query<Target>().Count().ShouldBe(5);
            }
        }


        [Fact]
        public async Task pass_in_current_connection_and_transaction_with_externally_controlled_tx_boundaries_async()
        {
            var newTargets = Target.GenerateRandomData(5).ToArray();

            using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                await conn.OpenAsync();
                var tx = conn.BeginTransaction();


                var cmd = conn.CreateCommand("delete from mt_doc_target");
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync();





                using (var session = theStore.OpenSession(new SessionOptions
                {
                    Transaction = tx,
                    OwnsTransactionLifecycle = false
                }))
                {
                    session.Store(newTargets);
                    await session.SaveChangesAsync();
                }

                // To prove the isolation here
                using (var query = theStore.QuerySession())
                {
                    (await query.Query<Target>().CountAsync()).ShouldBe(100);
                }

                await tx.CommitAsync();
            }

            // All the old should be gone, then the new put back on top
            using (var query = theStore.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(5);
            }
        }
    }
}