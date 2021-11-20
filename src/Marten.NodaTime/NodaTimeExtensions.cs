using System;
using System.Data;
using Marten.Services;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using NodaTime.Serialization.SystemTextJson;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.NodaTime
{
    public static class NodaTimeExtensions
    {
        /// <summary>
        /// Sets up NodaTime mappings for the PostgreSQL date/time types.
        /// By default it will configure either the underlying JSON.NET or System.Text.Json serializers.
        /// </summary>
        /// <param name="storeOptions">Store options that you're extending</param>
        /// <param name="shouldConfigureJsonSerializer">Sets if NodaTime configuration should be setup for the current serializer. Set value to false if you're using a different serializer type or you'd like to maintain your own configuration.</param>
        /// <exception cref="NotSupportedException">Thrown if the current serializer is not supported for automatic configuration.</exception>
        public static void UseNodaTime(this StoreOptions storeOptions, bool shouldConfigureJsonSerializer = true)
        {
            SetNodaTimeMappings();
            NpgsqlConnection.GlobalTypeMapper.UseNodaTime();

            if (!shouldConfigureJsonSerializer) return;

            var serializer = storeOptions.Serializer();

            switch (serializer)
            {
                case JsonNetSerializer jsonNetSerializer:
                    jsonNetSerializer.Customize(s =>
                    {
                        s.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                    });
                    break;
                case SystemTextJsonSerializer systemTextJsonSerializer:
                    systemTextJsonSerializer.Customize(s =>
                    {
                        s.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                    });
                    break;
                default:
                    throw new NotSupportedException("Current serializer cannot be automatically configured for NodaTime. Set shouldConfigureJsonSerializer to false if you're using your own serializer.");
            }

            storeOptions.Serializer(serializer);
        }

        public static void SetNodaTimeMappings()
        {
            NpgsqlTypeMapper.Mappings.AddRange(
                new NpgsqlTypeMapping[] {
                    // Date/time types
    #pragma warning disable 618 // NpgsqlDateTime is obsolete, remove in 7.0
                    new(NpgsqlDbType.Timestamp,   DbType.DateTime,       "timestamp without time zone", typeof(DateTime), typeof(NpgsqlDateTime), typeof(LocalDateTime)),
    #pragma warning disable 618
                    new(NpgsqlDbType.TimestampTz, DbType.DateTimeOffset, "timestamp with time zone",    typeof(DateTimeOffset), typeof(Instant), typeof(ZonedDateTime)),
                    new(NpgsqlDbType.Date,        DbType.Date,           "date",                        typeof(NpgsqlDate), typeof(LocalDate)
    #if NET6_0_OR_GREATER
                    , typeof(DateOnly)
    #endif
                    ),
                    new(NpgsqlDbType.Time,        DbType.Time,     "time without time zone", typeof(LocalTime)
    #if NET6_0_OR_GREATER
                    , typeof(TimeOnly)
    #endif
                    ),
                    new(NpgsqlDbType.TimeTz,      DbType.Object,   "time with time zone", typeof(LocalTime), typeof(LocalTime)),
                    new(NpgsqlDbType.Interval,    DbType.Object,   "interval", typeof(TimeSpan), typeof(NpgsqlTimeSpan), typeof(Period), typeof(Duration)),

                    new(NpgsqlDbType.Array | NpgsqlDbType.Timestamp,   DbType.Object, "timestamp without time zone[]"),
                    new(NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, DbType.Object, "timestamp with time zone[]"),
                    new(NpgsqlDbType.Range | NpgsqlDbType.Timestamp,   DbType.Object, "tsrange"),
                    new(NpgsqlDbType.Range | NpgsqlDbType.TimestampTz, DbType.Object, "tstzrange"),
                    new(NpgsqlDbType.Multirange | NpgsqlDbType.Timestamp,   DbType.Object, "tsmultirange"),
                    new(NpgsqlDbType.Multirange | NpgsqlDbType.TimestampTz, DbType.Object, "tstzmultirange"),
            });
        }
    }
}
