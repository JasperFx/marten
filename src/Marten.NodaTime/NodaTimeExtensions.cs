using System;
using System.Linq;
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
            NpgsqlConnection.GlobalTypeMapper.UseNodaTime();

            if (shouldConfigureJsonSerializer)
            {
                var serializer = storeOptions.Serializer();
                if(serializer is JsonNetSerializer jsonNetSerializer)
                {
                    jsonNetSerializer.Customize(s =>
                    {
                        s.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                    });
                }
                else if (serializer is SystemTextJsonSerializer systemTextJsonSerializer)
                {
                    systemTextJsonSerializer.Customize(s =>
                    {
                        s.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
                    });
                }
                else
                    throw new NotSupportedException("Current serializer cannot be automatically configured for Nodatime. Set shouldConfigureJsonSerializer to false if you're using your own serializer.");

                storeOptions.Serializer(serializer);
            }
        }
    }
}
