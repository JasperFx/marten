using System;
using System.Threading.Tasks;
using System.Transactions;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// #5611. For an aggregate projected with <c>ProjectionLifecycle.Async</c>,
/// <c>FetchForWriting(id)</c> brackets its three reads in
/// <c>begin transaction isolation level repeatable read read only</c> … <c>end</c> so they share one
/// snapshot — correct and necessary, see #5604. It emitted that bracket unconditionally, including on
/// a session already inside a transaction the CALLER owns, and PostgreSQL's two possible answers were
/// both wrong:
///
/// <list type="bullet">
/// <item><b>The caller's transaction has already run a statement:</b> <c>begin transaction isolation
/// level …</c> is refused with <c>25001: SET TRANSACTION ISOLATION LEVEL must be called before any
/// query</c>. That error lands inside the caller's transaction block and aborts it, so the session is
/// finished and the reported cause names an isolation level the caller never asked for.</item>
/// <item><b>The caller's transaction has run nothing yet:</b> silent. <c>begin</c> is ignored with a
/// warning, the isolation change is accepted, and the trailing <c>end</c> COMMITS the caller's
/// transaction. Later writes autocommit one at a time, the caller's own <c>Rollback()</c> throws
/// <c>InvalidOperationException: This NpgsqlTransaction has completed</c>, and the data it meant to
/// discard is already permanent.</item>
/// </list>
///
/// <para>
/// A fetch opens no transaction of its own, so a transaction the caller owns is left to the caller.
/// </para>
/// </summary>
public class Bug_5611_fetch_must_not_commit_a_caller_owned_transaction: OneOffConfigurationsContext
{
    private Guid _streamId;

    private async Task<Guid> withAnAsyncAggregateAndAStreamAsync()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Schema.For<User>();
        });

        // Every table these tests touch is created HERE, on an ordinary session. Nothing below may
        // depend on DDL that would itself be inside -- and roll back with -- the caller's transaction.
        _streamId = Guid.NewGuid();
        await using var setup = theStore.LightweightSession();
        setup.Events.StartStream<SimpleAggregate>(_streamId, new AEvent(), new BEvent(), new CEvent());
        setup.Store(new User { Id = Guid.NewGuid(), FirstName = "creates mt_doc_user" });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        return _streamId;
    }

    [Fact]
    public async Task an_external_transaction_survives_a_fetch_that_follows_a_write()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);

            // On the unfixed code this threw SessionTransactionUnusableException wrapping
            // `25001: SET TRANSACTION ISOLATION LEVEL must be called before any query`, because the
            // caller's transaction had already run the insert above.
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId, ct);
            stream.StartingVersion.ShouldBe(3);
            stream.Aggregate.ShouldNotBeNull();
        }

        await tx.RollbackAsync(ct);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task an_external_transaction_survives_a_fetch_that_precedes_any_write()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            // The fetch goes FIRST, so the caller's transaction has run no statement yet and PostgreSQL
            // accepts the bracket instead of refusing it. This is the silent case: on the unfixed code
            // the trailing `end` committed the caller's transaction right here.
            await session.Events.FetchForWriting<SimpleAggregate>(streamId, ct);

            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);
        }

        // On the unfixed code this threw `This NpgsqlTransaction has completed; it is no longer usable.`
        await tx.RollbackAsync(ct);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task a_session_transaction_begun_by_the_caller_survives_a_fetch()
    {
        // Marten's own TransactionalConnection counts too: once the caller has begun a transaction on
        // the session, the fetch's `end` would commit it, and the SaveChangesAsync that follows then
        // failed with `This NpgsqlTransaction has completed; it is no longer usable.`
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            await session.BeginTransactionAsync(ct);

            await session.Events.FetchForWriting<SimpleAggregate>(streamId, ct);

            session.Store(new User { Id = marker, FirstName = "committed by SaveChanges, not by the fetch" });
            await session.SaveChangesAsync(ct);
        }

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task an_ambient_transaction_scope_survives_a_fetch()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        using (new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var session = theStore.LightweightSession(SessionOptions.ForCurrentTransaction());

            await session.Events.FetchForWriting<SimpleAggregate>(streamId, ct);

            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);

            // Deliberately NOT calling scope.Complete(), so the scope rolls back on dispose.
        }

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task the_batched_fetch_respects_a_caller_owned_transaction_too()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            var batch = session.CreateBatchQuery();
            var query = batch.Events.FetchForWriting<SimpleAggregate>(streamId);
            await batch.Execute(ct);

            var stream = await query;
            stream.StartingVersion.ShouldBe(3);
            stream.Aggregate.ShouldNotBeNull();

            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);
        }

        await tx.RollbackAsync(ct);

        await using var querySession = theStore.QuerySession();
        (await querySession.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task a_batched_fetch_inside_a_caller_owned_transaction_need_not_be_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx));

        var batch = session.CreateBatchQuery();
        batch.Load<User>(Guid.NewGuid());
        var query = batch.Events.FetchForWriting<SimpleAggregate>(streamId);
        await batch.Execute(ct);

        var stream = await query;
        stream.StartingVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();

        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task a_caller_at_repeatable_read_gets_one_snapshot_without_marten_emitting_anything()
    {
        // A caller's own transaction at RepeatableRead: the reads share its snapshot, and it survives.
        var ct = TestContext.Current.CancellationToken;
        var streamId = await withAnAsyncAggregateAndAStreamAsync();
        var marker = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            var stream = await session.Events.FetchForWriting<SimpleAggregate>(streamId, ct);
            stream.StartingVersion.ShouldBe(3);
            stream.Aggregate.ShouldNotBeNull();
            stream.Aggregate.ACount.ShouldBe(1);
            stream.Aggregate.BCount.ShouldBe(1);
            stream.Aggregate.CCount.ShouldBe(1);

            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);
        }

        await tx.RollbackAsync(ct);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }
}
