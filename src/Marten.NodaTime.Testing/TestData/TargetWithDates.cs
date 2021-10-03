using System;
using NodaTime;

namespace Marten.NodaTime.Testing.TestData
{
    public class TargetWithDates: IEquatable<TargetWithDates>
    {
        public Guid Id { get; set; }
        public DateTime DateTime { get; set; }
        public DateTime? NullableDateTime { get; set; }
        public DateTimeOffset DateTimeOffset { get; set; }
        public DateTimeOffset? NullableDateTimeOffset { get; set; }

        public LocalDate LocalDate { get; set; }
        public LocalDate? NullableLocalDate { get; set; }
        public LocalDateTime LocalDateTime { get; set; }
        public LocalDateTime? NullableLocalDateTime { get; set; }
        public Instant InstantUTC { get; set; }
        public Instant? NullableInstantUTC { get; set; }

        internal static TargetWithDates Generate(DateTime? defaultDateTime = null)
        {
            var dateTime = defaultDateTime ?? DateTime.UtcNow;
            var localDateTime = LocalDateTime.FromDateTime(dateTime);
            var instant = Instant.FromDateTimeUtc(dateTime.ToUniversalTime());

            return new TargetWithDates
            {
                Id = Guid.NewGuid(),
                DateTime = dateTime,
                NullableDateTime = dateTime,
                DateTimeOffset = dateTime,
                NullableDateTimeOffset = dateTime,
                LocalDate = localDateTime.Date,
                NullableLocalDate = localDateTime.Date,
                LocalDateTime = localDateTime,
                NullableLocalDateTime = localDateTime,
                InstantUTC = instant,
                NullableInstantUTC = instant
            };
        }

        public bool Equals(TargetWithDates other)
        {
            if (other == null)
            {
                return false;
            }

            return DateTime == other.DateTime
                && NullableDateTime == other.NullableDateTime
                && DateTimeOffset == other.DateTimeOffset
                && NullableDateTimeOffset == other.NullableDateTimeOffset
                && LocalDate == other.LocalDate
                && NullableLocalDate == other.NullableLocalDate
                && LocalDateTime == other.LocalDateTime
                && NullableLocalDateTime == other.NullableLocalDateTime;
        }
    }
}
