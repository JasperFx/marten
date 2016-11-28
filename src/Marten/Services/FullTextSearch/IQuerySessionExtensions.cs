using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Services.FullTextSearch
{
    // Idea borrowed from: https://gist.github.com/phillip-haydon/167e490778b653ea1a2349d83f33daad

    public static class IQuerySessionExtensions
    {
        public static IList<T> FullTextSearch<T>(this IQuerySession instance, string phrase)
        {
            var searchMap = instance.DocumentStore.Schema.FullTextSearch.Map.Keys.OfType<SearchMap<T>>().FirstOrDefault();

            if (searchMap == null)
            {
                throw new InvalidOperationException($"No full text search configuration for {typeof(T).Name}");
            }

            return instance.Query<T>($"WHERE {searchMap.VectorName} @@ plainto_tsquery(:Phrase)", new { Phrase = phrase });
        }
    }
}