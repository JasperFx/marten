using System;
using System.Data.Common;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public class IncludeSelector<TSearched, TIncluded> : BasicSelector, ISelector<TSearched> where TIncluded : class
    {
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly IResolver<TIncluded> _resolver;

        public static string[] ToSelectFields(string tableAlias, IDocumentMapping includedMapping, ISelector<TSearched> inner)
        {
            var innerFields = inner.SelectFields();
            var outerFields = includedMapping.SelectFields().Select(x => $"{tableAlias}.{x}");

            return innerFields.Concat(outerFields).ToArray();
        }

        public IncludeSelector(string tableAlias, IDocumentMapping includedMapping, Action<TIncluded> callback, ISelector<TSearched> inner, IResolver<TIncluded> resolver)
            : base(ToSelectFields(tableAlias, includedMapping, inner))
        {
            _callback = callback;
            _inner = inner;
            _resolver = resolver;

            StartingIndex = _inner.SelectFields().Length;
        }

        public int StartingIndex { get; }

        public TSearched Resolve(DbDataReader reader, IIdentityMap map)
        {
            var included = _resolver.Resolve(StartingIndex, reader, map);
            _callback(included);

            return _inner.Resolve(reader, map);
        }


    }
}