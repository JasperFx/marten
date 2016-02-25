using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using NpgsqlTypes;

namespace Marten.Util
{
    public static class TypeMappings
    {
        private static readonly Dictionary<Type, string> PgTypes = new Dictionary<Type, string>
        {
            {typeof (int), "integer"},
            {typeof (long), "bigint"},
            {typeof (Guid), "uuid"},
            {typeof (string), "varchar"},
            {typeof (Boolean), "boolean"},
            {typeof (double), "double precision"},
            {typeof (decimal), "decimal"},
            {typeof (DateTime), "date"},
            {typeof(float), "decimal" },
            {typeof (DateTimeOffset), "timestamp with time zone"}
        };

        private static readonly MethodInfo _getNgpsqlDbTypeMethod;

        static TypeMappings()
        {
            var type = Type.GetType("Npgsql.TypeHandlerRegistry, Npgsql");
            _getNgpsqlDbTypeMethod = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(
                    x =>
                        x.Name == "ToNpgsqlDbType" && x.GetParameters().Count() == 1 &&
                        x.GetParameters().Single().ParameterType == typeof (Type));
        }

        public static string ConvertSynonyms(string type)
        {
            switch (type.ToLower())
            {
                case "character varying":
                case "varchar":
                    return "varchar";

                case "boolean":
                case "Boolean":
                    return "boolean";

                case "decimal":
                case "numeric":
                    return "decimal";
            }


            return type;
        }

        public static NpgsqlDbType ToDbType(Type type)
        {
            if (type.IsNullable()) return ToDbType(type.GetInnerTypeFromNullable());

            if (type == typeof (DateTime)) return NpgsqlDbType.Date;

            return (NpgsqlDbType) _getNgpsqlDbTypeMethod.Invoke(null, new object[] {type});
        }

        public static string GetPgType(Type memberType)
        {
            if (memberType.IsEnum) return "integer";

            if (memberType.IsNullable())
            {
                return GetPgType(memberType.GetInnerTypeFromNullable());
            }

            return PgTypes[memberType];
        }

        public static bool HasTypeMapping(Type memberType)
        {
            // more complicated later
            return PgTypes.ContainsKey(memberType) || memberType.IsEnum;
        }

        public static string ApplyCastToLocator(this string locator, Type memberType)
        {
            if (memberType.IsEnum)
            {
                return "({0})::int".ToFormat(locator);
            }

            if (!PgTypes.ContainsKey(memberType))
                throw new ArgumentOutOfRangeException(nameof(memberType),
                    "There is not a known Postgresql cast for member type " + memberType.FullName);

            return "CAST({0} as {1})".ToFormat(locator, PgTypes[memberType]);
        }
    }
}