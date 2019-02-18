using System;
using System.Collections.Generic;
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
        public void can_query_document()
        {
            StoreOptions(_ => _.UseNodaTime());

            var dateTime = new DateTime(636815202809001827);// DateTime.UtcNow;
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
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC < instantUTC.PlusTicks(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC <= instantUTC.PlusTicks(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC > instantUTC.PlusTicks(-1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.InstantUTC >= instantUTC.PlusTicks(-1)),

                    // Nullable Instant UTC
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC == instantUTC),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC < instantUTC.PlusTicks(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC <= instantUTC.PlusTicks(1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC > instantUTC.PlusTicks(-1)),
                    query.Query<TargetWithDates>().FirstOrDefault(d => d.NullableInstantUTC >= instantUTC.PlusTicks(-1))
                };

                results.ShouldAllBe(x => x.Equals(testDoc));
            }
        }
    }
}