using System;
using System.Threading.Tasks;
using System.Transactions;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
///     #5914. One <c>SaveChangesAsync</c> is one batch, and a statement's error belongs to the operation that
///     queued that statement. <c>OperationPage.ApplyCallbacksAsync</c> postprocessed operation <c>i</c> and then
///     called <c>NextResultAsync()</c> — which is where statement <c>i + 1</c>'s error surfaces — and asked
///     operation <c>i</c> to translate it. Under <see cref="EventAppendMode.QuickWithServerTimestamps" /> a start
///     of a new stream followed by a <c>FetchForWriting</c> append that loses its version check (MT003) came out
///     as <see cref="ExistingStreamIdCollisionException" /> naming the NEW stream, so a retry policy on
///     <see cref="ConcurrencyException" /> never fired.
///
///     An error raised before the first result set comes out of <c>ExecuteReaderAsync</c> instead, where every
///     operation in the batch was offered it in order and the first that recognised it won — so a duplicate
///     <c>StartStream</c> behind a fresh one under the per-event start route reported the fresh stream.
/// </summary>
public class Bug_5914_batch_exception_attribution: OneOffConfigurationsContext
{
    public sealed record Started(string Name);

    public sealed record Incremented(int By);

    public sealed record Counter(Guid Id, int Total)
    {
        public static Counter Create(Started _) => new(Guid.Empty, 0);
        public Counter Apply(Incremented e) => this with { Total = Total + e.By };
    }

    public enum Lifetime
    {
        /// <summary>The default session: AutoClosingLifetime</summary>
        AutoClosing,

        /// <summary>An ordinary session pinned by BeginTransactionAsync(): TransactionalConnection</summary>
        Pinned,

        /// <summary>SessionOptions.ForTransaction: ExternalTransaction</summary>
        External,

        /// <summary>SessionOptions.ForCurrentTransaction: AmbientTransactionLifetime</summary>
        Ambient
    }

    private DocumentStore BuildStore(EventAppendMode mode, Lifetime lifetime = Lifetime.AutoClosing)
    {
        return StoreOptions(opts =>
        {
            // Rich reads `actual` out of the violation's Detail, which Npgsql redacts without this
            opts.Connection(ConnectionSource.ConnectionString + ";Include Error Detail=true");
            opts.Events.AppendMode = mode;
        });
    }

    private static async Task<Guid> StartExistingStreamAsync(DocumentStore store)
    {
        var existing = Guid.NewGuid();
        await using var setup = store.LightweightSession();
        setup.Events.StartStream<Counter>(existing, new Started("existing"));
        await setup.SaveChangesAsync();
        return existing;
    }

    private static async Task CommitBehindTheFetchAsync(DocumentStore store, Guid existing)
    {
        // Never enlisted in the ambient scope: this is somebody else's committed write
        using var suppress = new TransactionScope(TransactionScopeOption.Suppress,
            TransactionScopeAsyncFlowOption.Enabled);
        await using var other = store.LightweightSession();
        other.Events.Append(existing, new Incremented(10));
        await other.SaveChangesAsync();
        suppress.Complete();
    }

    private static async Task<bool> StreamExistsAsync(DocumentStore store, Guid id)
    {
        await using var query = store.QuerySession();
        return await query.Events.FetchStreamStateAsync(id) != null;
    }

    private static void ShouldBeTheFetchedStreamsConflict(Exception thrown, Guid existing)
    {
        var ex = thrown.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
        ex.Id.ShouldBe(existing);
        ex.AggregateType.ShouldBe(typeof(Counter));
        ex.Message.ShouldContain("expected 1 but was 2");
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task a_conflict_behind_a_new_stream_is_the_fetched_streams_concurrency_exception(EventAppendMode mode)
    {
        // The issue's own case
        var store = BuildStore(mode);
        var existing = await StartExistingStreamAsync(store);

        var newId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream<Counter>(newId, new Started("new"));
        var stream = await session.Events.FetchForWriting<Counter>(existing);
        stream.AppendOne(new Incremented(1));

        await CommitBehindTheFetchAsync(store, existing);

        var thrown = await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        ShouldBeTheFetchedStreamsConflict(thrown, existing);

        (await StreamExistsAsync(store, newId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task a_conflict_ahead_of_a_new_stream_is_the_fetched_streams_concurrency_exception(EventAppendMode mode)
    {
        var store = BuildStore(mode);
        var existing = await StartExistingStreamAsync(store);

        await using var session = store.LightweightSession();
        var stream = await session.Events.FetchForWriting<Counter>(existing);
        stream.AppendOne(new Incremented(1));
        var newId = Guid.NewGuid();
        session.Events.StartStream<Counter>(newId, new Started("new"));

        await CommitBehindTheFetchAsync(store, existing);

        var thrown = await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
        ShouldBeTheFetchedStreamsConflict(thrown, existing);

        (await StreamExistsAsync(store, newId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task a_duplicate_start_behind_a_new_stream_is_a_collision_on_the_duplicate(EventAppendMode mode)
    {
        // #5454's mapping must survive: a genuine duplicate start is still a collision, on the right id
        var store = BuildStore(mode);
        var existing = await StartExistingStreamAsync(store);

        var newId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream<Counter>(newId, new Started("new"));
        session.Events.StartStream<Counter>(existing, new Started("duplicate"));

        var ex = await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => session.SaveChangesAsync());
        ex.Id.ShouldBe(existing);

        (await StreamExistsAsync(store, newId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task a_duplicate_start_ahead_of_a_new_stream_is_a_collision_on_the_duplicate(EventAppendMode mode)
    {
        var store = BuildStore(mode);
        var existing = await StartExistingStreamAsync(store);

        var newId = Guid.NewGuid();
        await using var session = store.LightweightSession();
        session.Events.StartStream<Counter>(existing, new Started("duplicate"));
        session.Events.StartStream<Counter>(newId, new Started("new"));

        var ex = await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => session.SaveChangesAsync());
        ex.Id.ShouldBe(existing);

        (await StreamExistsAsync(store, newId)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Lifetime.Pinned, EventAppendMode.Quick)]
    [InlineData(Lifetime.Pinned, EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(Lifetime.Pinned, EventAppendMode.Rich)]
    [InlineData(Lifetime.External, EventAppendMode.Quick)]
    [InlineData(Lifetime.External, EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(Lifetime.External, EventAppendMode.Rich)]
    [InlineData(Lifetime.Ambient, EventAppendMode.Quick)]
    [InlineData(Lifetime.Ambient, EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(Lifetime.Ambient, EventAppendMode.Rich)]
    public async Task every_connection_lifetime_attributes_the_conflict_to_the_fetched_stream(Lifetime lifetime,
        EventAppendMode mode)
    {
        // The three lifetimes that keep a connection open ran the batch through their own ExecuteReaderAsync,
        // which translated a failure with the application's and Marten's built-in transforms BEFORE any
        // operation was offered it.
        var store = BuildStore(mode, lifetime);
        var existing = await StartExistingStreamAsync(store);

        foreach (var conflictFirst in new[] { true, false })
        {
            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            NpgsqlTransaction tx = null;
            TransactionScope scope = null;

            SessionOptions options;
            switch (lifetime)
            {
                case Lifetime.External:
                    await conn.OpenAsync();
                    tx = await conn.BeginTransactionAsync();
                    options = SessionOptions.ForTransaction(tx);
                    break;
                case Lifetime.Ambient:
                    // TransactionScope defaults to Serializable, where the race is a 40001 instead
                    scope = new TransactionScope(TransactionScopeOption.Required,
                        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                        TransactionScopeAsyncFlowOption.Enabled);
                    options = SessionOptions.ForCurrentTransaction();
                    break;
                default:
                    options = new SessionOptions();
                    break;
            }

            try
            {
                await using var session = store.LightweightSession(options);
                if (lifetime == Lifetime.Pinned) await session.BeginTransactionAsync(System.Threading.CancellationToken.None);

                var newId = Guid.NewGuid();
                if (!conflictFirst) session.Events.StartStream<Counter>(newId, new Started("new"));
                var stream = await session.Events.FetchForWriting<Counter>(existing);
                stream.AppendOne(new Incremented(1));
                if (conflictFirst) session.Events.StartStream<Counter>(newId, new Started("new"));

                await CommitBehindTheFetchAsync(store, existing);

                var thrown = await Should.ThrowAsync<ConcurrencyException>(() => session.SaveChangesAsync());
                var ex = thrown.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
                ex.Id.ShouldBe(existing);
                ex.AggregateType.ShouldBe(typeof(Counter));
            }
            finally
            {
                if (tx != null) await tx.RollbackAsync();
                scope?.Dispose();
            }
        }
    }
}
