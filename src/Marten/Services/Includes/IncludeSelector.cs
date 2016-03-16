using System;
using System.Data.Common;
using System.Linq;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public class IncludeSelector<TSearched, TIncluded> : ISelector<TSearched> where TIncluded : class
    {
        private readonly string _tableAlias;
        private readonly IDocumentMapping _includedMapping;
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly IResolver<TIncluded> _resolver;

        public IncludeSelector(string tableAlias, IDocumentMapping includedMapping, Action<TIncluded> callback, ISelector<TSearched> inner, IResolver<TIncluded> resolver)
        {
            _tableAlias = tableAlias;
            _includedMapping = includedMapping;
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

        public string[] SelectFields()
        {
            var innerFields = _inner.SelectFields();
            var outerFields = _includedMapping.SelectFields().Select(x => $"{_tableAlias}.{x}");

            return innerFields.Concat(outerFields).ToArray();
        }
    }
}