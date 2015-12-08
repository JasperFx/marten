using System.Collections.Generic;
using System.Linq;

namespace Marten.Util
{
    public static class SerializerExtensions
    {
        public static IEnumerable<T> FromJson<T>(this ISerializer serializer, IEnumerable<string> jsonData)
        {
            return jsonData.Select(serializer.FromJson<T>);
        }


    }
}