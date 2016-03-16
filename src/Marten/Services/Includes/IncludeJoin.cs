using System;
using Marten.Linq;
using Marten.Schema;

namespace Marten.Services.Includes
{
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
}