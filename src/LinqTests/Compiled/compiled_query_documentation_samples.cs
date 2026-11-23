using System.IO;
using System.Threading.Tasks;
using Marten;

namespace LinqTests.Compiled;

// Compile-only samples for docs/documents/querying/compiled-queries.md. Nothing here runs as a test;
// each call is covered by compiled_queries.cs.
public class compiled_query_documentation_samples
{
    #region sample_compiled_query_as_json
    public async Task compiled_query_as_json(IQuerySession session, Stream destination)
    {
        // A list query as a JSON array string
        var json = await session.ToJsonMany(new FindJsonOrderedUsersByUsername { FirstName = "Jeremy" });

        // Or written straight to a stream. Returns the number of documents written
        var count = await session.StreamJsonMany(new FindJsonOrderedUsersByUsername { FirstName = "Jeremy" },
            destination);

        // A single document query as JSON, or null if nothing matched
        var one = await session.ToJsonOne(new FindJsonUserByUsername { Username = "jdm" });

        // Or written straight to a stream. Returns false if nothing matched
        var found = await session.StreamJsonOne(new FindJsonUserByUsername { Username = "jdm" }, destination);
    }
    #endregion
}
