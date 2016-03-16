using System;
using System.Data.Common;
using System.Linq;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
    public enum JoinType
    {
        Inner,
        LeftOuter
    }

    public interface IIncludeJoin
    {
        string JoinText { get; }
        string TableAlias { get; }
        ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner);
    }

    public class IncludeJoin<T> : IIncludeJoin where T : class
    {
        public string JoinText { get; }
        private readonly IDocumentMapping _mapping;
        private readonly Action<T> _callback;

        public IncludeJoin(IDocumentMapping mapping, string joinText, string tableAlias, Action<T> callback)
        {
            JoinText = joinText;
            _mapping = mapping;
            _callback = callback;

            TableAlias = tableAlias;
        }

        public string TableAlias { get; }

        public ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner)
        {
            return new IncludeSelector<TSearched, T>(TableAlias, _mapping, _callback, inner, schema.ResolverFor<T>());
        }
    }

    public class IncludeSelector<TSearched, TIncluded> : ISelector<TSearched> where TIncluded : class
    {
        private readonly string _tableAlias;
        private readonly IDocumentMapping _includedMapping;
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly IResolver<TIncluded> _resolver;
        private readonly int _startingIndex;

        public IncludeSelector(string tableAlias, IDocumentMapping includedMapping, Action<TIncluded> callback, ISelector<TSearched> inner, IResolver<TIncluded> resolver)
        {
            _tableAlias = tableAlias;
            _includedMapping = includedMapping;
            _callback = callback;
            _inner = inner;
            _resolver = resolver;

            _startingIndex = _inner.SelectFields().Length;
        }

        public TSearched Resolve(DbDataReader reader, IIdentityMap map)
        {
            var included = _resolver.Resolve(_startingIndex, reader, map);
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