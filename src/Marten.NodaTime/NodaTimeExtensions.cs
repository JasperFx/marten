using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Services;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using Npgsql;

namespace Marten.NodaTime
{
    public static class NodaTimeExtensions
    {
        public static void UseNodaTime(this StoreOptions storeOptions)
        {
            NpgsqlConnection.GlobalTypeMapper.UseNodaTime();

            storeOptions.Linq.MethodCallParsers.Add(new SimpleNodaTimeEqualsParser());
            storeOptions.Linq.MethodCallParsers.Add(new SimpleNodaTimeNotEqualsParser());

            JsonLocatorField.ContainmentOperatorTypes.Add(typeof(LocalDate));
            JsonLocatorField.ContainmentOperatorTypes.Add(typeof(LocalDateTime));

            //JsonLocatorField.TimespanZTypes.Add(typeof(Instant));
            //JsonLocatorField.TimespanZTypes.Add(typeof(Instant?));
            //JsonLocatorField.TimespanTypes.Add(typeof(LocalDateTime));
            //JsonLocatorField.TimespanTypes.Add(typeof(LocalDateTime?));

            //JsonLocatorField.TimespanZTypes.Add(typeof(ZonedDateTime));
            //JsonLocatorField.TimespanZTypes.Add(typeof(ZonedDateTime?));
            //JsonLocatorField.TimespanZTypes.Add(typeof(OffsetDateTime));
            //JsonLocatorField.TimespanZTypes.Add(typeof(OffsetDateTime?));

            var serializer = new JsonNetSerializer();
            serializer.Customize(s => s.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb));

            storeOptions.Serializer(serializer);
        }
    }
}