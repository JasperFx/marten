using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public class IncludeSelector<TSearched, TIncluded> : BasicSelector, ISelector<TSearched>
    {
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly IDocumentStorage<TIncluded> storage;

        public static string[] ToSelectFields(string tableAlias, IQueryableDocument includedMapping, ISelector<TSearched> inner)
        {
            var innerFields = inner.SelectFields();
            var outerFields = includedMapping.SelectFields().Select(x => $"{tableAlias}.{x}");

            return innerFields.Concat(outerFields).ToArray();
        }

        public IncludeSelector(string tableAlias, IQueryableDocument includedMapping, Action<TIncluded> callback, ISelector<TSearched> inner, IDocumentStorage<TIncluded> documentStorage)
            : base(ToSelectFields(tableAlias, includedMapping, inner))
        {
            _callback = callback;
            _inner = inner;
            storage = documentStorage;

            StartingIndex = _inner.SelectFields().Length;
        }

        public int StartingIndex { get; }

        public TSearched Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var included = storage.Resolve(StartingIndex, reader, map);
            _callback(included);

            return _inner.Resolve(reader, map, stats);
        }


        public async Task<TSearched> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var included = await storage.ResolveAsync(StartingIndex, reader, map, token).ConfigureAwait(false);

            _callback(included);

            return await _inner.ResolveAsync(reader, map, stats, token).ConfigureAwait(false);
        }
    }
}