using System;
using System.Collections.Generic;

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
            {typeof(Boolean), "Boolean"}
        };
    }
}