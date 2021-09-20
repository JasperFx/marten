using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class OpeningAQuerySession
    {
        public static async Task open_it()
        {
            #region sample_opening_querysession

            using var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");
            });

            using var session = store.QuerySession();

            var badIssues = await session.Query<Issue>()
                .Where(x => x.Tags.Contains("bad"))
                .ToListAsync();

            #endregion
        }
    }
}
