using System;
using System.Collections.Generic;
using System.Data;
using Marten.Services;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using NodaTime.Serialization.SystemTextJson;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.NodaTimePlugin;

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
        SetNodaTimeTypeMappings();
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
                throw new NotSupportedException(
                    "Current serializer cannot be automatically configured for NodaTime. Set shouldConfigureJsonSerializer to false if you're using your own serializer.");
        }

        storeOptions.Serializer(serializer);
    }

    public static void SetNodaTimeTypeMappings()
    {
        foreach (var mapping in Mappings)
        {
            NpgsqlTypeMapper.Mappings[mapping.Key] = mapping.Value;
        }
    }

    private static readonly Dictionary<NpgsqlDbType, NpgsqlTypeMapping> Mappings =
        new()
        {
            // Date/time types
            {
                NpgsqlDbType.Timestamp, new(NpgsqlDbType.Timestamp, DbType.DateTime, "timestamp without time zone",
                    typeof(DateTime),
                    typeof(LocalDateTime))
            },
            {
                NpgsqlDbType.TimestampTz, new(NpgsqlDbType.TimestampTz, DbType.DateTimeOffset,
                    "timestamp with time zone",
                    typeof(DateTimeOffset),
                    typeof(Instant), typeof(ZonedDateTime))
            },
            { NpgsqlDbType.Date, new(NpgsqlDbType.Date, DbType.Date, "date", typeof(LocalDate), typeof(DateOnly)) },
            {
                NpgsqlDbType.Time,
                new(NpgsqlDbType.Time, DbType.Time, "time without time zone", typeof(LocalTime), typeof(TimeOnly))
            },
            {
                NpgsqlDbType.TimeTz,
                new(NpgsqlDbType.TimeTz, DbType.Object, "time with time zone", typeof(LocalTime), typeof(LocalTime))
            },
            {
                NpgsqlDbType.Interval, new(NpgsqlDbType.Interval, DbType.Object, "interval", typeof(TimeSpan),
                    typeof(Period),
                    typeof(Duration))
            },
            {
                NpgsqlDbType.Array | NpgsqlDbType.Timestamp,
                new(NpgsqlDbType.Array | NpgsqlDbType.Timestamp, DbType.Object, "timestamp without time zone[]")
            },
            {
                NpgsqlDbType.Array | NpgsqlDbType.TimestampTz,
                new(NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, DbType.Object, "timestamp with time zone[]")
            },
            {
                NpgsqlDbType.Range | NpgsqlDbType.Timestamp,
                new(NpgsqlDbType.Range | NpgsqlDbType.Timestamp, DbType.Object, "tsrange")
            },
            {
                NpgsqlDbType.Range | NpgsqlDbType.TimestampTz,
                new(NpgsqlDbType.Range | NpgsqlDbType.TimestampTz, DbType.Object, "tstzrange")
            },
            {
                NpgsqlDbType.Multirange | NpgsqlDbType.Timestamp,
                new(NpgsqlDbType.Multirange | NpgsqlDbType.Timestamp, DbType.Object, "tsmultirange")
            },
            {
                NpgsqlDbType.Multirange | NpgsqlDbType.TimestampTz,
                new(NpgsqlDbType.Multirange | NpgsqlDbType.TimestampTz, DbType.Object, "tstzmultirange")
            }
        };
}
