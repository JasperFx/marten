using System.Collections.Generic;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);

        string[] SelectFields();
    }

    public static class SelectorExtensions
    {
        public static IList<T> Read<T>(this ISelector<T> selector, DbDataReader reader, IIdentityMap map)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                list.Add(selector.Resolve(reader, map));
            }

            return list;
        }
    }
}