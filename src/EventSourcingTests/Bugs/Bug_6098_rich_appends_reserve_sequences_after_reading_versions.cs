using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #6098: a Rich append reserved its sequence numbers and then read the stream's version, in one batch
/// before the commit. When another session committed to the stream between the two, the late session
/// took the next version under an older sequence number, and the stream read back out of version order.
/// It now reads the version first, so a commit in that gap is the ordinary version conflict.
/// </summary>
public class Bug_6098_rich_appends_reserve_sequences_after_reading_versions: OneOffConfigurationsContext
{
    [Fact]
    public async Task an_append_committed_right_after_another_reserves_its_sequence_numbers_never_lands_under_them()
    {
        StoreOptions(opts => opts.Events.AppendMode = EventAppendMode.Rich);

        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await using (var setup = theStore.LightweightSession())
        {
            setup.Events.StartStream<SimpleAggregate>(id, new AEvent());
            await setup.SaveChangesAsync(ct);
        }

        var pause = new PauseAfterTheSequenceReservation();
        Task late;

        await using (var paused = theStore.LightweightSession())
        {
            paused.Logger = pause;
            paused.Events.Append(id, new CEvent());
            late = paused.SaveChangesAsync(ct);

            // The late session has reserved its sequence numbers. Another one appends and commits
            await pause.IsWaitingAsync();

            await using (var other = theStore.LightweightSession())
            {
                other.Events.Append(id, new BEvent());
                await other.SaveChangesAsync(ct);
            }

            await pause.ReleaseAsync();

            // It read the version before the other commit, so it loses on the version, as any concurrent
            // append does, rather than committing version 3 under a sequence number older than version 2's
            await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => late);
        }

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(id, token: ct);

        events.Select(x => x.Version).ShouldBe([1, 2]);
        events.Select(x => x.Data.GetType()).ShouldBe([typeof(AEvent), typeof(BEvent)]);
    }

    /// <summary>
    /// Holds the Rich pre-commit batch on an advisory lock right after its sequence reservation, before the
    /// batch's next statement starts (and takes its snapshot), until released.
    /// </summary>
    private sealed class PauseAfterTheSequenceReservation: IMartenSessionLogger
    {
        private static long _nextKey = Random.Shared.Next(1, 1 << 30);
        private readonly long _key = Interlocked.Increment(ref _nextKey);
        private NpgsqlConnection? _gate;

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            if (_gate != null) return;

            var commands = batch.BatchCommands.Cast<NpgsqlBatchCommand>().ToList();
            if (!commands.Any(x => x.CommandText.Contains("mt_streams"))) return;

            var reservation = commands.FirstOrDefault(x =>
                x.CommandText.Contains("nextval(") && x.CommandText.Contains("mt_events_sequence"));
            if (reservation == null) return;

            _gate = new NpgsqlConnection(ConnectionSource.ConnectionString);
            _gate.Open();
            using (var take = _gate.CreateCommand())
            {
                take.CommandText = $"select pg_advisory_lock({_key})";
                take.ExecuteNonQuery();
            }

            // OFFSET 0 keeps the reservation in its own subquery, and the correlated lock is evaluated per
            // reserved row, so the wait comes after nextval and before the statement ends
            reservation.CommandText =
                $"select r.nextval from ({reservation.CommandText} offset 0) r " +
                $"where (select true from pg_advisory_xact_lock_shared({_key} + r.nextval * 0))";
        }

        public async Task IsWaitingAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();
            await using var waiting = conn.CreateCommand();
            waiting.CommandText = "select exists (select 1 from pg_locks where locktype = 'advisory' and not granted " +
                                  $"and objid::bigint = {_key} and database = (select oid from pg_database where datname = current_database()))";

            var giveUp = DateTime.UtcNow.AddSeconds(30);
            while (!(bool)(await waiting.ExecuteScalarAsync())!)
            {
                if (DateTime.UtcNow > giveUp) throw new TimeoutException("The append never reached the pause after its sequence reservation");
                await Task.Delay(10);
            }
        }

        public async Task ReleaseAsync()
        {
            if (_gate == null) return;

            await using (var release = _gate.CreateCommand())
            {
                release.CommandText = $"select pg_advisory_unlock({_key})";
                await release.ExecuteNonQueryAsync();
            }

            await _gate.DisposeAsync();
        }

        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }
}
