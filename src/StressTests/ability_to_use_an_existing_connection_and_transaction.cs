using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;
using IsolationLevel = System.Data.IsolationLevel;

namespace StressTests;

public class ability_to_use_an_existing_connection_and_transaction: IntegrationContext
{
    private readonly Target[] targets = Target.GenerateRandomData(100).ToArray();

    public ability_to_use_an_existing_connection_and_transaction(DefaultStoreFixture fixture,
        ITestOutputHelper output = null): base(fixture)
    {
    }

    protected override async Task fixtureSetup()
    {
        await theStore.BulkInsertDocumentsAsync(targets);
    }


    public void samples(IDocumentStore store, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        #region sample_passing-in-existing-connections-and-transactions

        // Use an existing connection, but Marten still controls the transaction lifecycle
        var session1 = store.LightweightSession(SessionOptions.ForConnection(connection));


        // Enlist in an existing Npgsql transaction, but
        // choose not to allow the session to own the transaction
        // boundaries
        var session3 = store.LightweightSession(SessionOptions.ForTransaction(transaction));

        // Enlist in the current, ambient transaction scope
        using var scope = new TransactionScope();
        var session4 = store.LightweightSession(SessionOptions.ForCurrentTransaction());

        #endregion
    }


    [Fact]
    public void can_open_serializable_sync()
    {
        using var session = theStore.LightweightSession(IsolationLevel.Serializable);
        session.Connection.State.ShouldBe(ConnectionState.Open);
    }

    [Fact]
    public async Task can_query_sync_with_session_enlisted_in_transaction_scope()
    {
        using var scope = new TransactionScope();
        using var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction());

        var aTarget = targets.First();

        var targetFromQuery = session.Query<Target>().Single(x => x.Id == aTarget.Id);
        targetFromQuery.Id.ShouldBe(aTarget.Id);

        var targetFromLoad = await session.LoadAsync<Target>(aTarget.Id);
        targetFromLoad.Id.ShouldBe(aTarget.Id);

        scope.Complete();
    }

    [Fact]
    public async Task can_query_async_with_session_enlisted_in_transaction_scope()
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await using var session = await theStore.LightweightSerializableSessionAsync(SessionOptions.ForCurrentTransaction());

        var aTarget = targets.First();

        var targetFromQuery = await session.Query<Target>().SingleAsync(x => x.Id == aTarget.Id);
        targetFromQuery.Id.ShouldBe(aTarget.Id);

        var targetFromLoad = await session.LoadAsync<Target>(aTarget.Id);
        targetFromLoad.Id.ShouldBe(aTarget.Id);

        scope.Complete();
    }

    [Fact]
    public async Task enlist_in_transaction_scope()
    {
        using (var scope = new TransactionScope())
        {
            using (var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction()))
            {
                session.Store(Target.Random(), Target.Random());
                await session.SaveChangesAsync();
            }

            // should not yet be committed
            using (var session = theStore.QuerySession())
            {
                //See https://github.com/npgsql/npgsql/issues/1483 - Npgsql by default is enlisting
                session.Query<Target>().Count().ShouldBe(102);
            }

            scope.Complete();
        }

        // should be 2 additional targets
        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Count().ShouldBe(102);
        }
    }


    [Fact]
    public async Task enlist_in_transaction_scope_by_transaction()
    {
        using (var scope = new TransactionScope())
        {
            using (var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction()))
            {
                session.Store(Target.Random(), Target.Random());
                await session.SaveChangesAsync();
            }

            // should not yet be committed
            using (var session = theStore.QuerySession())
            {
                //See https://github.com/npgsql/npgsql/issues/1483 - Npgsql by default is enlisting
                session.Query<Target>().Count().ShouldBe(102);
            }

            scope.Complete();
        }

        // should be 2 additional targets
        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Count().ShouldBe(102);
        }
    }

    [Fact]
    public async Task pass_in_current_connection_and_transaction()
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

            using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx, true)))
            {
                session.Store(newTargets);
                await session.SaveChangesAsync();
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

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            var tx = conn.BeginTransaction();

            var cmd = conn.CreateCommand("delete from mt_doc_target");
            cmd.Transaction = tx;
            await cmd.ExecuteNonQueryAsync();

            // To prove the isolation here
            await using (var query = theStore.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(100);
            }

            await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx, true)))
            {
                session.Store(newTargets);
                await session.SaveChangesAsync();
            }
        }

        // All the old should be gone, then the new put back on top
        await using (var query = theStore.QuerySession())
        {
            (await query.Query<Target>().CountAsync()).ShouldBe(5);
        }
    }

    [Fact]
    public async Task pass_in_current_connection_and_transaction_with_externally_controlled_tx_boundaries()
    {
        var newTargets = Target.GenerateRandomData(5).ToArray();

        using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            conn.Open();
            var tx = conn.BeginTransaction();

            var cmd = conn.CreateCommand("delete from mt_doc_target");
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();

            using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
            {
                session.Store(newTargets);
                await session.SaveChangesAsync();
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

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            var tx = conn.BeginTransaction();

            var cmd = conn.CreateCommand("delete from mt_doc_target");
            cmd.Transaction = tx;
            await cmd.ExecuteNonQueryAsync();

            await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
            {
                session.Store(newTargets);
                await session.SaveChangesAsync();
            }

            // To prove the isolation here
            await using (var query = theStore.QuerySession())
            {
                (await query.Query<Target>().CountAsync()).ShouldBe(100);
            }

            await tx.CommitAsync();
        }

        // All the old should be gone, then the new put back on top
        await using (var query = theStore.QuerySession())
        {
            (await query.Query<Target>().CountAsync()).ShouldBe(5);
        }
    }
}
