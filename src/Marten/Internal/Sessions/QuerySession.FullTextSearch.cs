using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

#nullable enable

namespace Marten.Internal.Sessions
{
    public partial class QuerySession
    {
        public IReadOnlyList<TDoc> Search<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToListAsync(token);
        }

        public IReadOnlyList<TDoc> PlainTextSearch<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToListAsync(token);
        }

        public IReadOnlyList<TDoc> PhraseSearch<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToListAsync(token);
        }

        public IReadOnlyList<TDoc> WebStyleSearch<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm,
            string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToListAsync(token);
        }

    }
}
