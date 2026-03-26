using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Projections;
using Marten.MemoryPackPlugin;
using Shouldly;
using JasperFx;
using Xunit;

namespace Marten.MemoryPack.Tests;

public class MemoryPackEventTests: IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5442;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=30";

    private const string SchemaName = "memorypack_tests";
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.UseMemoryPackSerialization();
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.DisableNpgsqlLogging = true;
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task basic_round_trip_with_quick_append()
    {
        var streamId = Guid.NewGuid();
        var started = new TripStarted
        {
            TripId = streamId,
            DriverName = "Alice",
            StartedAt = DateTimeOffset.UtcNow,
            StartLatitude = 40.7128,
            StartLongitude = -74.0060
        };

        var pickedUp = new PassengerPickedUp
        {
            TripId = streamId,
            PassengerName = "Bob",
            PickedUpAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        var ended = new TripEnded
        {
            TripId = streamId,
            EndedAt = DateTimeOffset.UtcNow.AddMinutes(20),
            EndLatitude = 40.7589,
            EndLongitude = -73.9851,
            FareAmount = 25.50m
        };

        // Write events
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId, started, pickedUp, ended);
            await session.SaveChangesAsync();
        }

        // Read events back
        await using (var session = _store.LightweightSession())
        {
            var events = await session.Events.FetchStreamAsync(streamId);

            events.Count.ShouldBe(3);

            var readStarted = events[0].Data.ShouldBeOfType<TripStarted>();
            readStarted.DriverName.ShouldBe("Alice");
            readStarted.StartLatitude.ShouldBe(40.7128);
            readStarted.StartLongitude.ShouldBe(-74.0060);

            var readPickedUp = events[1].Data.ShouldBeOfType<PassengerPickedUp>();
            readPickedUp.PassengerName.ShouldBe("Bob");

            var readEnded = events[2].Data.ShouldBeOfType<TripEnded>();
            readEnded.FareAmount.ShouldBe(25.50m);
            readEnded.EndLatitude.ShouldBe(40.7589);
        }
    }

    [Fact]
    public async Task bulk_insert_events_with_memory_pack()
    {
        var streams = new List<StreamAction>();

        for (int i = 0; i < 100; i++)
        {
            var streamId = Guid.NewGuid();
            var events = new object[]
            {
                new TripStarted
                {
                    TripId = streamId,
                    DriverName = $"Driver-{i}",
                    StartedAt = DateTimeOffset.UtcNow,
                    StartLatitude = 40.0 + i * 0.01,
                    StartLongitude = -74.0 + i * 0.01
                },
                new TripEnded
                {
                    TripId = streamId,
                    EndedAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    FareAmount = 10.0m + i
                }
            };

            streams.Add(StreamAction.Start(_store.Events, streamId, events));
        }

        await _store.BulkInsertEventsAsync(streams);

        // Verify counts
        await using var session = _store.LightweightSession();
        var stats = await session.Events.QueryAllRawEvents()
            .CountAsync();

        stats.ShouldBe(200); // 100 streams × 2 events each
    }

    [Fact]
    public async Task live_aggregation_with_memory_pack()
    {
        var streamId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Trip>(streamId,
                new TripStarted
                {
                    TripId = streamId,
                    DriverName = "Charlie",
                    StartedAt = DateTimeOffset.UtcNow
                },
                new PassengerPickedUp
                {
                    TripId = streamId,
                    PassengerName = "Dave",
                    PickedUpAt = DateTimeOffset.UtcNow.AddMinutes(3)
                },
                new PassengerPickedUp
                {
                    TripId = streamId,
                    PassengerName = "Eve",
                    PickedUpAt = DateTimeOffset.UtcNow.AddMinutes(7)
                },
                new TripEnded
                {
                    TripId = streamId,
                    EndedAt = DateTimeOffset.UtcNow.AddMinutes(25),
                    FareAmount = 42.00m
                });

            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession())
        {
            var trip = await session.Events.AggregateStreamAsync<Trip>(streamId);

            trip.ShouldNotBeNull();
            trip.DriverName.ShouldBe("Charlie");
            trip.PassengerCount.ShouldBe(2);
            trip.FareAmount.ShouldBe(42.00m);
            trip.IsActive.ShouldBe(false);
        }
    }

    [Fact]
    public async Task schema_uses_bytea_column()
    {
        // Write at least one event to ensure schema is fully created
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new TripStarted
            {
                TripId = Guid.NewGuid(), DriverName = "Test", StartedAt = DateTimeOffset.UtcNow
            });
            await session.SaveChangesAsync();
        }

        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = '{SchemaName}'
              AND table_name = 'mt_events'
              AND column_name = 'data'";

        var dataType = (string?)await cmd.ExecuteScalarAsync();
        dataType.ShouldBe("bytea");
    }
}

public class MemoryPackValidationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5442;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=30";

    [Fact]
    public async Task registering_non_memorypackable_event_throws()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "memorypack_validation";
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.UseMemoryPackSerialization();
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.DisableNpgsqlLogging = true;
        });

        // Attempting to start a stream with a non-[MemoryPackable] event should throw
        await using var session = store.LightweightSession();
        Should.Throw<InvalidOperationException>(() =>
        {
            session.Events.StartStream(Guid.NewGuid(), new NonMemoryPackableEvent { Name = "test" });
        }).Message.ShouldContain("[MemoryPackable]");
    }
}
