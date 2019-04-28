using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.NodaTime.Testing.TestData;
using Marten.Testing;
using NodaTime;
using Shouldly;
using Xunit;

namespace Marten.NodaTime.Testing.Acceptance
{
    public class noda_time_acceptance : IntegratedFixture
    {
        public void noda_time_default_setup()
        {
            // SAMPLE: noda_time_default_setup
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // sets up NodaTime handling
                _.UseNodaTime();
            });
            // ENDSAMPLE
        }

        public void noda_time_setup_without_json_net_serializer_configuration()
        {
            // SAMPLE: noda_time_setup_without_json_net_serializer_configuration
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Serializer<CustomJsonSerializer>();

                // sets up NodaTime handling
                _.UseNodaTime(shouldConfigureJsonNetSerializer: false);
            });
            // ENDSAMPLE
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

        [Fact]
        public void can_query_document_with_noda_time_types()
        {
            StoreOptions(_ => _.UseNodaTime());

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
                };

                results.ShouldAllBe(x => x.Equals(testDoc));
            }
        }

        [Fact]
        public void cannot_query_document_clr_datetime_types()
        {
            StoreOptions(_ => _.UseNodaTime());

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
                Should.Throw<NotSupportedException>
                (
                    () => query.Query<TargetWithDates>().FirstOrDefault(d => d.DateTime == dateTime),
                    "The CLR type System.DateTime isn't natively supported by Npgsql or your PostgreSQL. To use it with a PostgreSQL composite you need to specify DataTypeName or to map it, please refer to the documentation."
                );
            }
        }

        private class CustomJsonSerializer : ISerializer
        {
            public EnumStorage EnumStorage => throw new NotImplementedException();

            public Casing Casing => throw new NotImplementedException();

            public CollectionStorage CollectionStorage => throw new NotImplementedException();

            public T FromJson<T>(TextReader reader)
            {
                throw new NotImplementedException();
            }

            public object FromJson(Type type, TextReader reader)
            {
                throw new NotImplementedException();
            }

            public string ToCleanJson(object document)
            {
                throw new NotImplementedException();
            }

            public void ToJson(object document, TextWriter writer)
            {
                throw new NotImplementedException();
            }

            public string ToJson(object document)
            {
                throw new NotImplementedException();
            }
        }
    }
}