#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    public Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default)
    {
        return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToListAsync(token);
    }

    public Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default)
    {
        return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToListAsync(token);
    }

    public Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default)
    {
        return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToListAsync(token);
    }

    public Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default)
    {
        return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToListAsync(token);
    }
}
