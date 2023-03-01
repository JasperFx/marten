using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class OpeningASessionAsync
{
    public static async Task open_it()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");
        });

        #region sample_opening_session_async

        await using var session =
            await store.LightweightSerializableSessionAsync(SessionOptions.ForConnectionString("another connection string"));

        var openIssues = await session.Query<Issue>()
            .Where(x => x.Tags.Contains("open"))
            .ToListAsync();

        #endregion
    }
}
