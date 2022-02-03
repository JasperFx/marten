using System;
using System.Reflection;

namespace Marten.Linq.SqlProjection
{
    public static class SqlProjectionExtensions
    {
        public static readonly MethodInfo MethodInfo = typeof(SqlProjectionExtensions)
            .GetMethod(nameof(SqlProjection),
                BindingFlags.Public | BindingFlags.Static);

        public static T SqlProjection<T>(this object doc, string sql, params object[] parameters)
        {
            throw new NotSupportedException(
                $"{nameof(SqlProjection)} extension method can only be used in Marten Linq queries.");
        }
    }
}
