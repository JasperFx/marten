using System;
using System.Collections.Generic;
using FubuCore;

namespace Marten
{
    public static class TypeMappings
    {
        public static readonly Dictionary<Type, string> PgTypes = new Dictionary<Type, string>
        {
            {typeof (int), "integer"},
            {typeof (long), "bigint"},
            {typeof(Guid), "uuid"},
            {typeof(string), "varchar"},
            {typeof(Boolean), "Boolean"},
            {typeof(double), "double precision"},
            {typeof(decimal), "decimal"},
            {typeof(DateTime), "date"},
            {typeof(DateTimeOffset), "timestamp with time zone"}
        };

        public static bool HasTypeMapping(Type memberType)
        {
            // more complicated later
            return PgTypes.ContainsKey(memberType);
        }

        public static string ApplyCastToLocator(this string locator, Type memberType)
        {
            if (memberType.IsEnum)
            {
                return "({0})::int".ToFormat(locator);
            }

            if (!TypeMappings.PgTypes.ContainsKey(memberType))
                throw new ArgumentOutOfRangeException(nameof(memberType),
                    "There is not a known Postgresql cast for member type " + memberType.FullName);

            return "CAST({0} as {1})".ToFormat(locator, TypeMappings.PgTypes[memberType]);
        }
    }
}