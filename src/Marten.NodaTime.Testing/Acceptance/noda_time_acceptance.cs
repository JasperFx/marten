using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.NodaTimePlugin.Testing.TestData;
using Marten.Services.Json;
using Marten.Testing.Harness;
using NodaTime;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.NodaTimePlugin.Testing.Acceptance;

public class MonsterSlayed
{
    public Guid QuestId { get; set; }
    public string Name { get; set; }

    protected bool Equals(MonsterSlayed other)
    {
        return QuestId.Equals(other.QuestId) && Name == other.Name;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((MonsterSlayed) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(QuestId, Name);
    }
}


public class noda_time_acceptance: OneOffConfigurationsContext
{
    public void noda_time_default_setup()
    {
        #region sample_noda_time_default_setup
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);

            // sets up NodaTime handling
            _.UseNodaTime();
        });
        #endregion
    }

    public void noda_time_setup_without_json_net_serializer_configuration()
    {
        #region sample_noda_time_setup_without_json_net_serializer_configuration
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);

            _.Serializer<CustomJsonSerializer>();

            // sets up NodaTime handling
            _.UseNodaTime(shouldConfigureJsonSerializer: false);
        });
        #endregion
    }

    [Fact]
    public void throws_on_unsupported_serializer()
    {
        Assert.Throws<NotSupportedException>(() =>
            StoreOptions(_ =>
            {
                _.Serializer<CustomJsonSerializer>();

                _.UseNodaTime();
            }));
    }


    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task can_insert_document(SerializerType serializerType)
    {
        StoreOptions(opts =>
        {
            if (serializerType == SerializerType.Newtonsoft)
            {
                opts.UseNewtonsoftForSerialization();
            }

            opts.UseNodaTime();
        });

        var testDoc = TargetWithDates.Generate();

        await using var session = theStore.LightweightSession();
        session.Insert(testDoc);
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var docFromDb = query.Query<TargetWithDates>().FirstOrDefault(d => d.Id == testDoc.Id);

        docFromDb.ShouldNotBeNull();
        docFromDb.Equals(testDoc).ShouldBeTrue();
    }

    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task can_query_document_with_noda_time_types(SerializerType serializerType)
    {
        StoreOptions(opts =>
        {
            if (serializerType == SerializerType.Newtonsoft)
            {
                opts.UseNewtonsoftForSerialization();
            }
            opts.UseNodaTime();
            opts.DatabaseSchemaName = "NodaTime";
            opts.Schema.For<TargetWithDates>()
                .Duplicate(x => x.NullableLocalDate);
        }, true);

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var dateTime = DateTime.UtcNow;
        var localDateTime = LocalDateTime.FromDateTime(dateTime);
        var instantUTC = Instant.FromDateTimeUtc(dateTime.ToUniversalTime());
        var testDoc = TargetWithDates.Generate(dateTime);

        await using var session = theStore.LightweightSession();
        session.Insert(testDoc);
        await session.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var results = new List<TargetWithDates>
        {
            // LocalDate
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDate == localDateTime.Date),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDate < localDateTime.Date.PlusDays(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDate <= localDateTime.Date.PlusDays(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDate > localDateTime.Date.PlusDays(-1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDate >= localDateTime.Date.PlusDays(-1)),

            //// Nullable LocalDate
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDate == localDateTime.Date),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDate < localDateTime.Date.PlusDays(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDate <= localDateTime.Date.PlusDays(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDate > localDateTime.Date.PlusDays(-1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDate >= localDateTime.Date.PlusDays(-1)),

            //// LocalDateTime
            //query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime == localDateTime),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime < localDateTime.PlusSeconds(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime <= localDateTime.PlusSeconds(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime > localDateTime.PlusSeconds(-1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime >= localDateTime.PlusSeconds(-1)),

            //// Nullable LocalDateTime
            //query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime == localDateTime),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime < localDateTime.PlusSeconds(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime <= localDateTime.PlusSeconds(1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime > localDateTime.PlusSeconds(-1)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime >= localDateTime.PlusSeconds(-1)),

            //// Instant UTC
            //query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC == instantUTC),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC < instantUTC.PlusTicks(1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC <= instantUTC.PlusTicks(1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC > instantUTC.PlusTicks(-1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC >= instantUTC.PlusTicks(-1000)),

            // Nullable Instant UTC
            //query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC == instantUTC),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC < instantUTC.PlusTicks(1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC <= instantUTC.PlusTicks(1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC > instantUTC.PlusTicks(-1000)),
            query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC >= instantUTC.PlusTicks(-1000))

        };

        results.ToArray().ShouldAllBe(x => x.Equals(testDoc));
    }

    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task can_append_and_query_events(SerializerType serializerType)
    {
        StoreOptions(opts =>
        {
            if (serializerType == SerializerType.Newtonsoft)
            {
                opts.UseNewtonsoftForSerialization();
            }
            opts.UseNodaTime();
        }, true);

        var startDate = DateTime.UtcNow;

        var streamId = Guid.NewGuid();

        var @event = new MonsterSlayed()
        {
            QuestId = Guid.NewGuid(),
            Name = "test"
        };

        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, @event);
        await session.SaveChangesAsync();

        var streamState = await session.Events.FetchStreamStateAsync(streamId);
        var streamState2 = await session.Events.FetchStreamStateAsync(streamId);
        var streamState3 = await session.Events.FetchStreamAsync(streamId, timestamp: startDate);
    }

    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task bug_1276_can_select_instant(SerializerType serializerType)
    {
        // NOTE: the cast/rounding scenario described by this comment won't happen on MacOS
        // since it doesn't provide nanosecond precision since High Sierra
        // https://github.com/golang/go/issues/22037
        //
        //
        // .NET date/time types have tick precision (100ns)
        // Postgres date/time types have microsecond precision
        //
        //
        // When an Instant is saved as a property in a document, it's converted to a string
        // using NodaTime.Text.InstantPattern.ExtendedIso, and it contains the full tick precision
        // e.g. "2025-11-23T20:36:25.9226214Z"
        //
        // When this document is queried, there are two scenarios:
        // 1. The full document is queried directly using LINQ, the Instant property is deserialized using
        //    the string value with the full tick precision.
        //
        // 2. The document is queried using LINQ and projected with Select with the Instant property being
        //    selected - the property string from the database is cast to a 'timestamp with time zone' postgresql
        //    type.
        //    This is where the value is truncated to microseconds and round half up is used on the
        //    tick remainder. It also results in a different string format so a fallback deserialization pattern
        //    is used.
        //
        // Rounding example:
        // SELECT
        //   CAST ('2025-11-23T20:36:25.9226214Z' AS TIMESTAMP WITH TIME ZONE) no_round,
        //   CAST ('2025-11-23T20:36:25.9226215Z' AS TIMESTAMP WITH TIME ZONE) round;
        //
        // will result in
        // no_round = 2025-11-23 20:36:25.922621 +00:00
        // round    = 2025-11-23 20:36:25.922622 +00:00
        //
        // This is why ShouldBeEqualWithDbPrecision assertion is used which does the following:
        // 1. truncates both Instants to microseconds and compares them
        // 2. allows for a 1 microsecond tolerance to account for the potential rounding of the remaining ticks

        StoreOptions(opts =>
        {
            switch (serializerType)
            {
                case SerializerType.Newtonsoft:
                    opts.UseNewtonsoftForSerialization();
                    break;
                case SerializerType.SystemTextJson:
                    opts.UseSystemTextJsonForSerialization();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(serializerType), serializerType, null);
            }

            opts.UseNodaTime();
        });

        var dateTime = DateTime.UtcNow;
        var instantUTC = Instant.FromDateTimeUtc(dateTime.ToUniversalTime());
        var testDoc = TargetWithDates.Generate(dateTime);

        using (var session = theStore.LightweightSession())
        {
            session.Insert(testDoc);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            var result = query
                .Query<TargetWithDates>()
                .Single(c => c.Id == testDoc.Id);

            var resultWithSelect = query.Query<TargetWithDates>()
                .Where(c => c.Id == testDoc.Id)
                .Select(c => new { c.Id, c.InstantUTC, c.NullableInstantUTC, c.NullInstantUTC })
                .Single();

            result.ShouldNotBeNull();
            result.Id.ShouldBe(testDoc.Id);
            result.NullInstantUTC.ShouldBeNull();
            result.InstantUTC.ShouldBeEqualWithDbPrecision(instantUTC);
            result.NullableInstantUTC!.Value.ShouldBeEqualWithDbPrecision(instantUTC);

            resultWithSelect.ShouldNotBeNull();
            resultWithSelect.Id.ShouldBe(testDoc.Id);
            resultWithSelect.NullInstantUTC.ShouldBeNull();
            resultWithSelect.InstantUTC.ShouldBeEqualWithDbPrecision(instantUTC);
            resultWithSelect.NullableInstantUTC!.Value.ShouldBeEqualWithDbPrecision(instantUTC);
        }
    }

    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task can_index_noda_time_types(SerializerType serializerType)
    {
        StoreOptions(opts =>
        {
            if (serializerType == SerializerType.Newtonsoft)
            {
                opts.UseNewtonsoftForSerialization();
            }
            opts.UseNodaTime();
            opts.DatabaseSchemaName = "NodaTime";
            opts.Schema.For<TargetWithDates>()
                .Duplicate(x => x.LocalDate)
                .Duplicate(x => x.LocalDateTime)
                .Duplicate(x => x.InstantUTC);
        }, true);

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        // This will apply schema changes, creating indexes on NodaTime fields.
        // Previously this would fail with "functions in index expression must be marked IMMUTABLE"
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    private class CustomJsonSerializer: ISerializer
    {
        public EnumStorage EnumStorage => throw new NotSupportedException();

        public Casing Casing => throw new NotSupportedException();

        public CollectionStorage CollectionStorage => throw new NotSupportedException();

        public NonPublicMembersStorage NonPublicMembersStorage => throw new NotSupportedException();
        public string ToJsonWithTypes(object document)
        {
            throw new NotSupportedException();
        }

        public ValueCasting ValueCasting { get; } = ValueCasting.Relaxed;

        public T FromJson<T>(Stream stream)
        {
            throw new NotSupportedException();
        }

        public T FromJson<T>(DbDataReader reader, int index)
        {
            throw new NotSupportedException();
        }

        public ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object FromJson(Type type, Stream stream)
        {
            throw new NotSupportedException();
        }

        public object FromJson(Type type, DbDataReader reader, int index)
        {
            throw new NotSupportedException();
        }

        public ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public string ToCleanJson(object document)
        {
            throw new NotSupportedException();
        }

        public string ToJson(object document)
        {
            throw new NotSupportedException();
        }
    }
}
