using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Testing;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures
{
    public abstract class QueryFixture : MartenFixture
    {
        protected QueryList<IQueryable<Target>, IQueryable<Target>> Queries = new QueryList<IQueryable<Target>, IQueryable<Target>>(nameof(Queries), "");

        protected QueryFixture(string title)
        {
            Title = title;
            Queries.ReadFile(this);
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Queries")] string Query, out ResultSet Results)
        {
            var func = Queries.FuncFor(Query);


            using (var query = Store.QuerySession())
            {
                var results = func(query.Query<Target>()).ToList();
                Results = new ResultSet(results.Select(x => IdToName[x.Id]).ToArray());
            }
        }
    }
}