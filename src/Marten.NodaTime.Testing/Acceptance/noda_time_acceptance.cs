using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.NodaTime.Testing.TestData;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using NodaTime;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.NodaTime.Testing.Acceptance
{
    [Collection("noda_time_integration")]
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
            #endregion sample_noda_time_default_setup
        }

        public void noda_time_setup_without_json_net_serializer_configuration()
        {
            #region sample_noda_time_setup_without_json_net_serializer_configuration
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Serializer<CustomJsonSerializer>();

                // sets up NodaTime handling
                _.UseNodaTime(shouldConfigureJsonNetSerializer: false);
            });
            #endregion sample_noda_time_setup_without_json_net_serializer_configuration
        }

        [Fact]
        public void can_insert_document()
        {
            StoreOptions(_ => _.UseNodaTime());

            var testDoc = TargetWithDates.Generate();

            using (var session = theStore.OpenSession())
            {
                session.Insert(testDoc);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var docFromDb = query.Query<TargetWithDates>().FirstOrDefault(d => d.Id == testDoc.Id);

                docFromDb.ShouldNotBeNull();
                docFromDb.Equals(testDoc).ShouldBeTrue();
            }

        }

        //[Fact]
        public void can_query_document_with_noda_time_types()
        {
            StoreOptions(_ =>
            {
                _.UseNodaTime();
                _.DatabaseSchemaName = "NodaTime";
            });

            var dateTime = DateTime.UtcNow;
            var localDateTime = LocalDateTime.FromDateTime(dateTime);
            var instantUTC = Instant.FromDateTimeUtc(dateTime.ToUniversalTime());
            var testDoc = TargetWithDates.Generate(dateTime);

            using (var session = theStore.OpenSession())
            {
                session.Insert(testDoc);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
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
/*
                    //// LocalDateTime
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime == localDateTime),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime < localDateTime.PlusSeconds(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime <= localDateTime.PlusSeconds(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime > localDateTime.PlusSeconds(-1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.LocalDateTime >= localDateTime.PlusSeconds(-1)),

                    //// Nullable LocalDateTime
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime == localDateTime),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime < localDateTime.PlusSeconds(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime <= localDateTime.PlusSeconds(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime > localDateTime.PlusSeconds(-1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableLocalDateTime >= localDateTime.PlusSeconds(-1)),

                    //// Instant UTC
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC == instantUTC),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC < instantUTC.PlusTicks(1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC <= instantUTC.PlusTicks(1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC > instantUTC.PlusTicks(-1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC >= instantUTC.PlusTicks(-1000)),

                    // Nullable Instant UTC
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC == instantUTC),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC < instantUTC.PlusTicks(1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC <= instantUTC.PlusTicks(1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC > instantUTC.PlusTicks(-1000)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC >= instantUTC.PlusTicks(-1000))
                    */
                };

                results.ToArray().ShouldAllBe(x => x.Equals(testDoc));
            }
        }

        [Fact]
        public async Task can_append_and_query_events()
        {
            StoreOptions(_ => _.UseNodaTime());

            var startDate = DateTime.UtcNow;

            var streamId = Guid.NewGuid();

            var @event = new MonsterSlayed()
            {
                QuestId = Guid.NewGuid(),
                Name = "test"
            };

            using (var session = theStore.OpenSession())
            {
                session.Events.Append(streamId, @event);
                session.SaveChanges();

                var streamState = session.Events.FetchStreamState(streamId);
                var streamState2 = await session.Events.FetchStreamStateAsync(streamId);
                var streamState3 = session.Events.FetchStream(streamId, timestamp: startDate);
            }
        }

        [Fact]
        public void bug_1276_can_select_instant()
        {
            StoreOptions(_ => _.UseNodaTime());

            var dateTime = DateTime.UtcNow;
            var instantUTC = Instant.FromDateTimeUtc(dateTime.ToUniversalTime());
            var testDoc = TargetWithDates.Generate(dateTime);

            using (var session = theStore.OpenSession())
            {
                session.Insert(testDoc);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var resulta = query.Query<TargetWithDates>()
                    .Where(c => c.Id == testDoc.Id)
                    .Single();

                var result = query.Query<TargetWithDates>()
                    .Where(c => c.Id == testDoc.Id)
                    .Select(c => new { c.Id, c.InstantUTC })
                    .Single();

                result.ShouldNotBeNull();
                result.Id.ShouldBe(testDoc.Id);
                ShouldBeEqualWithDbPrecision(result.InstantUTC, instantUTC);
            }
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

        private static void ShouldBeEqualWithDbPrecision(Instant actual, Instant expected)
        {
            static Instant toDbPrecision(Instant date) => Instant.FromUnixTimeMilliseconds(date.ToUnixTimeMilliseconds() / 100 * 100);

            toDbPrecision(actual).ShouldBe(toDbPrecision(expected));
        }

        public noda_time_acceptance() : base("noda_time_integration")
        {
        }
    }
}
