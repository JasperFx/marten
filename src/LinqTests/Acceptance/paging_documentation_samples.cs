using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.CursorPaging;
using Marten.Testing.Documents;

namespace LinqTests.Acceptance;

// Compile-only samples for docs/documents/querying/linq/paging.md. Nothing here runs as a test;
// StreamPagedJsonArray() is covered by DocumentDbTests/Reading/Json/streaming_json_results.cs and
// ToJsonPageByCursorAsync() by DocumentDbTests/Reading/cursor_paging_tests.cs.
public class paging_documentation_samples
{
    #region sample_stream_paged_json_array
    public async Task stream_paged_json_array(IQuerySession session, Stream httpResponseStream)
    {
        var pageNumber = 3;
        var pageSize = 25;

        await session.Query<Target>()
            .Where(x => x.Flag)
            .OrderBy(x => x.Number)
            .StreamPagedJsonArray(httpResponseStream, pageNumber, pageSize);
    }
    #endregion

    #region sample_keyset_cursor_paging
    public async Task keyset_cursor_paging(IQuerySession session)
    {
        // First page: pass cursor: null
        var firstPage = await session.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 25);

        // firstPage.ItemsJson is a raw JSON array string: "[{...},{...},...]"
        // firstPage.NextCursor is an opaque token, or null on the last page

        // Next page: pass the previous page's cursor and the same ordering
        var secondPage = await session.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(firstPage.NextCursor, pageSize: 25);
    }
    #endregion
}
