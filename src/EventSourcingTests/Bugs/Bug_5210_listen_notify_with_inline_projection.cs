using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Operations;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Regression guard for https://github.com/JasperFx/marten/issues/5210.
//
// NotifyEventAppendedOperation is marked with the Weasel.Storage.NoDataReturnedCall
// marker, which tells OperationPage.ApplyCallbacksAsync to skip both PostprocessAsync
// AND reader.NextResultAsync() for that operation. But its SQL was
// `select pg_notify('mt_events_appended', '')` — a SELECT that DOES produce a
// one-row, one-void-column result set. The batched reader's cursor then fell one
// result set behind for every data-returning operation after it in the same
// SaveChanges. UnitOfWork.AllOperations puts the event operations (including the
// notify) ahead of the document operations, so any inline projection upsert or
// revisioned document update in the same transaction read the pg_notify row and
// blew up with:
//
//   System.InvalidCastException: Reading as 'System.Int64' is not supported for
//   fields having DataTypeName 'void'
//     at Weasel.Storage.NumericClosedShapeUpsertOperation`2.PostprocessAsync(...)
public class Bug_5210_listen_notify_with_inline_projection: BugIntegrationContext
{
    public record CounterIncremented(Guid CounterId);

    public class Counter
    {
        public Guid Id { get; set; }
        public int Count { get; set; }

        public static Counter Create(CounterIncremented e) => new() { Id = e.CounterId, Count = 1 };
        public void Apply(CounterIncremented e) => Count++;
    }

    public class VersionedDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    [Fact]
    public async Task inline_projection_in_same_transaction_as_listen_notify_append()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseListenNotifyForEventAppends = true;
            opts.Projections.Snapshot<Counter>(SnapshotLifecycle.Inline);
        });

        var counterId = Guid.NewGuid();
        theSession.Events.StartStream<Counter>(counterId, new CounterIncremented(counterId),
            new CounterIncremented(counterId));

        // Blew up here with the void-column InvalidCastException before the fix
        await theSession.SaveChangesAsync();

        var counter = await theSession.LoadAsync<Counter>(counterId);
        counter.ShouldNotBeNull();
        counter.Count.ShouldBe(2);

        // And again on a pure append to the existing stream (the aggregate upsert
        // still shares the batch with the notify)
        theSession.Events.Append(counterId, new CounterIncremented(counterId));
        await theSession.SaveChangesAsync();

        counter = await theSession.LoadAsync<Counter>(counterId);
        counter!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task revisioned_document_updated_in_same_unit_of_work_as_listen_notify_append()
    {
        // The reporter's second symptom: updating a numeric-revisioned document in
        // the same unit of work as an event append also read the pg_notify row.
        StoreOptions(opts =>
        {
            opts.Events.UseListenNotifyForEventAppends = true;
            opts.Schema.For<VersionedDoc>().UseNumericRevisions(true);
        });

        var doc = new VersionedDoc { Id = Guid.NewGuid(), Name = "initial" };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        doc.Name = "updated";
        theSession.Store(doc);
        theSession.Events.StartStream<Counter>(Guid.NewGuid(), new CounterIncremented(Guid.NewGuid()));

        // Blew up here with the void-column InvalidCastException before the fix
        await theSession.SaveChangesAsync();

        var reloaded = await theSession.LoadAsync<VersionedDoc>(doc.Id);
        reloaded.ShouldNotBeNull();
        reloaded.Name.ShouldBe("updated");
    }

    // Pins the NoDataReturnedCall contract for the notify statement itself: the SQL
    // must produce NO result set (so the skipped reader stays aligned), while still
    // actually delivering the NOTIFY to a listener.
    [Fact]
    public async Task notify_statement_produces_no_result_set_but_still_notifies()
    {
        var notificationReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var listener = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await listener.OpenAsync();
        listener.Notification += (_, args) => notificationReceived.TrySetResult(args.Channel);

        await using (var listen = listener.CreateCommand())
        {
            listen.CommandText = $"LISTEN {PostgresqlListenWakeup.DefaultChannel}";
            await listen.ExecuteNonQueryAsync();
        }

        await using var notifier = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await notifier.OpenAsync();
        await using (var cmd = notifier.CreateCommand())
        {
            cmd.CommandText = NotifyEventAppendedOperation.Sql;
            await using var reader = await cmd.ExecuteReaderAsync();

            // The whole point of #5210: no result set may come back from this statement
            reader.FieldCount.ShouldBe(0);
            (await reader.ReadAsync()).ShouldBeFalse();
            (await reader.NextResultAsync()).ShouldBeFalse();
        }

        // ... and the NOTIFY still goes out
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = listener.WaitAsync(cts.Token);
        var channel = await notificationReceived.Task.WaitAsync(cts.Token);
        channel.ShouldBe(PostgresqlListenWakeup.DefaultChannel);
    }
}
