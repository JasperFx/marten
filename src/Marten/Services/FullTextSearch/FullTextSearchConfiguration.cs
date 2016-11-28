using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Remotion.Linq.Clauses;

namespace Marten.Services.FullTextSearch
{
    public sealed class FullTextSearchConfiguration
    {
        private readonly DocumentSchema documentSchema;
        private readonly Dictionary<SearchMap, IDocumentMapping> map = new Dictionary<SearchMap, IDocumentMapping>();

        public FullTextSearchConfiguration(DocumentSchema documentSchema)
        {
            this.documentSchema = documentSchema;
        }

        public IReadOnlyDictionary<SearchMap, IDocumentMapping> Map => map;

        public FullTextSearchConfiguration Search<T>(Action<SearchMap<T>> configure)
        {
            var searchMap = SearchMap.Register<T>(configure);
            var storage = documentSchema.MappingFor(typeof(T));
            map.Add(searchMap, storage);
            return this;
        }

        public IEnumerable<FullTextSearchDDL> GetDDL()
        {
            return map.Select(x => new FullTextSearchDDL(x.Key, x.Value));
        }

        public bool Enabled => map.Any();
    }
}