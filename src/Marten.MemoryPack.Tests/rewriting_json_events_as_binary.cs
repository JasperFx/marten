using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using MemoryPack;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

// Not [BinaryEvent]: stored as JSON until a store opts it in
[MemoryPackable]
public partial record FareQuoted(Guid TripId, decimal Amount, string Note);

[Collection("Marten.MemoryPack")]
public class rewriting_json_events_as_binary: IAsyncLifetime
{
    private const string Schema = "memorypack_rewrite";

    private static DocumentStore storeFor(bool binary) => DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = Schema;
        opts.Events.AddEventType<FareQuoted>();
        if (binary) opts.Events.UseMemoryPackSerializer<FareQuoted>();
    });

    public async ValueTask InitializeAsync()
    {
        using var store = storeFor(binary: false);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public ValueTask DisposeAsync() => default;

    [Fact]
    public async Task events_written_as_json_are_stored_in_bdata_after_the_rewrite()
    {
        var tripId = Guid.NewGuid();
        using (var json = storeFor(binary: false))
        {
            await using var session = json.LightweightSession();
            session.Events.StartStream(tripId, new FareQuoted(tripId, 12.5m, new string('x', 200)));
            await session.SaveChangesAsync();
        }

        using var binary = storeFor(binary: true);
        var result = await binary.Advanced.RewriteStoredEventsAsync(x => x.IncludeType<FareQuoted>());

        var database = result.Databases.Single();
        database.Rewritten.ShouldBe(1);
        database.BytesAfter.ShouldBeLessThan(database.BytesBefore);

        await using (var conn = binary.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"select data::text, bdata is not null from {Schema}.mt_events", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            reader.GetString(0).ShouldBe("{}");
            reader.GetBoolean(1).ShouldBeTrue();
        }

        await using var query = binary.QuerySession();
        var events = await query.Events.FetchStreamAsync(tripId);
        events.Single().Data.ShouldBe(new FareQuoted(tripId, 12.5m, new string('x', 200)));
    }
}
