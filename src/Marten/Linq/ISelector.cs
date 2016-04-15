using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);

        Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);

        string[] SelectFields();

        string ToSelectClause(IDocumentMapping mapping);
    }

    public abstract class BasicSelector
    {
        private readonly string[] _selectFields;

        protected BasicSelector(params string[] selectFields)
        {
            _selectFields = selectFields;
        }

        public string[] SelectFields() => _selectFields;

        public string ToSelectClause(IDocumentMapping mapping)
        {
            return $"select {SelectFields().Join(", ")} from {mapping.Table.QualifiedName} as d";
        }
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

        public static async Task<IList<T>> ReadAsync<T>(this ISelector<T> selector, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(await selector.ResolveAsync(reader, map, token).ConfigureAwait(false));
            }

            return list;
        }
    }
}