using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class concurrent_start_stream_collision: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task losing_a_start_stream_race_throws_existing_stream_id_collision(EventAppendMode mode)
    {
        StoreOptions(opts => opts.Events.AppendMode = mode);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var first = theStore.LightweightSession(SessionOptions.ForTransaction(transaction)))
        {
            first.Events.StartStream(streamId, new RaceStarted("first"));
            await first.SaveChangesAsync();
        }

        var second = Task.Run(async () =>
        {
            await using var session = theStore.LightweightSession();
            session.Events.StartStream(streamId, new RaceStarted("second"));
            await session.SaveChangesAsync();
        });

        await Task.Delay(TimeSpan.FromSeconds(1));
        second.IsCompleted.ShouldBeFalse();

        await transaction.CommitAsync();

        await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => second);
    }
}

public sealed record RaceStarted(string By);
