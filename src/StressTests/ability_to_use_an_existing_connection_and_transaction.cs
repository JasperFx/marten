using System;
using System.Collections.Generic;
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
using IsolationLevel = System.Data.IsolationLevel;

namespace StressTests;

public class ability_to_use_an_existing_connection_and_transaction: IntegrationContext
{
    private readonly Target[] targets = Target.GenerateRandomData(100).ToArray();

    // Every assertion in this class is an UNFILTERED Target count — 100 seeded, 102 after storing
    // two, 5 after a truncate-and-replace — and fixtureSetup adds its 100 on top of whatever the
    // previous test left behind. So the tests that truncate leave the ones that count asserting
    // against 105, and each only held while it happened to run before its siblings. Exactly the
    // case #5070 added this hook for.
    //
    // The companion half is AssemblyInfo.cs: this fixes the ordering WITHIN the class, that one
    // stops sibling classes resetting the database underneath it. Neither alone is enough, and
    // nothing surfaced either until #5096 put the project in CI — four tests were passing only on
    // the supervisor's fresh-process retry, where each runs alone.
    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(Target)];

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

    // Was can_query_sync_with_session_enlisted_in_transaction_scope, asserting through
    // Query<T>().Single(...) — synchronous data access, which Marten 9.0 removed, so it threw
    // NotSupportedException on every run from 9.0 onward. Nobody saw it: the project had no CI job
    // until #5096.
    //
    // Rewritten rather than deleted, unlike its two sibling sync tests. This is the only coverage
    // of a plain LightweightSession enlisted in an ambient scope; the async test below it opens a
    // SERIALIZABLE session, so deleting this would have quietly dropped the default isolation level
    // from the enlistment tests.
    [Fact]
    public async Task can_query_with_session_enlisted_in_transaction_scope()
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await using var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction());

        var aTarget = targets.First();

        var targetFromQuery = await session.Query<Target>().SingleAsync(x => x.Id == aTarget.Id);
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

    // TransactionScopeAsyncFlowOption.Enabled, and async queries throughout.
    //
    // Without the flow option the ambient transaction does not follow an await: the continuation
    // after SaveChangesAsync resumes on a different thread, and the scope's Dispose then throws
    // "A TransactionScope must be disposed on the same thread that it was created." The sync
    // Query<T>().Count() calls were separately dead as of Marten 9.0. Both failed on every run
    // since, unseen, because the project was outside CI until #5096.
    [Fact]
    public async Task enlist_in_transaction_scope()
    {
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using (var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction()))
            {
                session.Store(Target.Random(), Target.Random());
                await session.SaveChangesAsync();
            }

            // should not yet be committed
            await using (var session = theStore.QuerySession())
            {
                //See https://github.com/npgsql/npgsql/issues/1483 - Npgsql by default is enlisting
                (await session.Query<Target>().CountAsync()).ShouldBe(102);
            }

            scope.Complete();
        }

        // should be 2 additional targets
        await using (var session = theStore.QuerySession())
        {
            (await session.Query<Target>().CountAsync()).ShouldBe(102);
        }
    }


    [Fact]
    public async Task enlist_in_transaction_scope_by_transaction()
    {
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using (var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction()))
            {
                session.Store(Target.Random(), Target.Random());
                await session.SaveChangesAsync();
            }

            // should not yet be committed
            await using (var session = theStore.QuerySession())
            {
                //See https://github.com/npgsql/npgsql/issues/1483 - Npgsql by default is enlisting
                (await session.Query<Target>().CountAsync()).ShouldBe(102);
            }

            scope.Complete();
        }

        // should be 2 additional targets
        await using (var session = theStore.QuerySession())
        {
            (await session.Query<Target>().CountAsync()).ShouldBe(102);
        }
    }

    // The synchronous pass_in_current_connection_and_transaction was deleted here: it was this test
    // with conn.Open() and Query<T>().Count() in place of the awaits, so once Marten 9.0 removed
    // synchronous data access it could only throw. Every assertion it made is made below.
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

    // Same story as above: the synchronous
    // pass_in_current_connection_and_transaction_with_externally_controlled_tx_boundaries was this
    // test with sync connection and query calls, dead since Marten 9.0 removed them, and deleted
    // here rather than rewritten into a copy of what follows.
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
