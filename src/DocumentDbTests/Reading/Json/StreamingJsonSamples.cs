using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace DocumentDbTests.Reading.Json;

// Documentation sample for docs/documents/querying/query-json.md. Compiled, not run: the behavior is covered by
// streaming_json_results and query_by_sql.
public class StreamingJsonSamples
{
    public async Task stream_raw_json(IQuerySession session, Stream destination, Guid userId,
        CancellationToken token)
    {
        #region sample_streaming_raw_json

        // Every document that matches, as one JSON array. Returns how many documents were written
        var count = await session.Query<User>()
            .Where(x => x.LastName == "Miller")
            .OrderBy(x => x.FirstName)
            .StreamJsonArray(destination, token);

        // The first matching document. Writes nothing and returns false when no document matches
        var found = await session.Query<User>()
            .Where(x => x.UserName == "jdm")
            .StreamJsonFirstOrDefault(destination, token);

        // One document by its id. Returns false when there is none
        found = await session.Json.StreamById<User>(userId, destination, token);

        // The results of your own SQL, as one JSON array
        count = await session.StreamJson<User>(destination, token,
            "where data ->> 'LastName' = ?", "Miller");

        #endregion
    }
}
