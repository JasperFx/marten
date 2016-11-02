using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector
    {
        string[] SelectFields();

        string ToSelectClause(IQueryableDocument mapping);
    }

    public interface ISelector<T> : ISelector
    {
        T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats);

        Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token);
    }

    public class StandInSelector<T> : ISelector<T>
    {
        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            throw new NotSupportedException();
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public string[] SelectFields()
        {
            throw new NotSupportedException();
        }

        public string ToSelectClause(IQueryableDocument mapping)
        {
            throw new NotSupportedException();
        }
    }

    public abstract class BasicSelector
    {
        private readonly string[] _selectFields;
        private readonly bool _distinct = false;

        protected BasicSelector(params string[] selectFields)
        {
            _selectFields = selectFields;
        }

        protected BasicSelector(bool distinct, params string[] selectFields)
        {
            _selectFields = selectFields;
            _distinct = distinct;
        }

        public string[] SelectFields() => _selectFields;

        public string ToSelectClause(IQueryableDocument mapping)
        {
            return $"select {(_distinct ? "distinct " : "")}{SelectFields().Join(", ")} from {mapping.Table.QualifiedName} as d";
        }
    }

    public static class SelectorExtensions
    {
        public static IList<T> Read<T>(this ISelector<T> selector, DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var list = new List<T>();

            while (reader.Read())
            {
                list.Add(selector.Resolve(reader, map, stats));
            }

            return list;
        }

        public static async Task<IList<T>> ReadAsync<T>(this ISelector<T> selector, DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                list.Add(await selector.ResolveAsync(reader, map, stats, token).ConfigureAwait(false));
            }

            return list;
        }
    }
}