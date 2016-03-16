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
        private readonly Action<T> _callback;

        public IncludeJoin(string joinText, string tableAlias, Action<T> callback)
        {
            JoinText = joinText;
            _callback = callback;

            TableAlias = tableAlias;
        }

        public string TableAlias { get; }

        public ISelector<TSearched> WrapSelector<TSearched>(IDocumentSchema schema, ISelector<TSearched> inner)
        {
            return new IncludeSelector<TSearched, T>(TableAlias, _callback, inner, schema.ResolverFor<T>());
        }
    }

    public class IncludeSelector<TSearched, TIncluded> : ISelector<TSearched> where TIncluded : class
    {
        private readonly string _tableAlias;
        private readonly Action<TIncluded> _callback;
        private readonly ISelector<TSearched> _inner;
        private readonly IResolver<TIncluded> _resolver;
        private readonly int _startingIndex;

        public IncludeSelector(string tableAlias, Action<TIncluded> callback, ISelector<TSearched> inner,
            IResolver<TIncluded> resolver)
        {
            _tableAlias = tableAlias;
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

            // Have resolver return the fields here.

            return
                innerFields.Concat(new[]
                {$"{_tableAlias}.data as {_tableAlias}_data", $"${_tableAlias}.id as {_tableAlias}_id"}).ToArray();
        }
    }
}