using System;
using System.Linq;
using Marten.Testing;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures
{
    [Obsolete("Let's get rid of this soon as its moving to xUnit")]
    public abstract class MatchedFixture : MartenFixture
    {
        protected QueryList<IQueryable<Target>, IQueryable<Target>> Sync = new QueryList<IQueryable<Target>, IQueryable<Target>>(nameof(Sync), "");

        public MatchedFixture(string title)
        {
            Title = title;
            Sync.ReadFile(this);
        }

        public override void SetUp()
        {
            base.SetUp();
            Targets = Target.GenerateRandomData(1000).ToArray();
        }

        protected Target[] Targets { get; set; }

        [FormatAs("Query {query}")]
        [ExposeAsTable("Ordered queries running synchronously")]
        public bool OrderedMatch([SelectionList("Sync")] string query)
        {
            var func = Sync.FuncFor(query);
            var expected = func(Targets.AsQueryable()).ToList().Select(x => x.Id).ToArray();

            Guid[] actuals;
            using (var session = Store.QuerySession())
            {
                actuals = func(session.Query<Target>()).ToList().Select(x => x.Id).ToArray();
            }

            StoryTellerAssert.Fail(expected.Length != actuals.Length, $"Wrong number of results. Expected {expected.Length}, but got {actuals.Length}");

            for (int i = 0; i < actuals.Length; i++)
            {
                if (actuals[i] != expected[i]) return false;
            }

            return true;
        }

        [FormatAs("Query {query}")]
        [ExposeAsTable("Unordered queries running synchronously")]
        public bool UnorderedMatch([SelectionList("Sync")] string query)
        {
            var func = Sync.FuncFor(query);
            var expected = func(Targets.AsQueryable()).ToList().OrderBy(x => x.Id).Select(x => x.Id).ToArray();

            Guid[] actuals;
            using (var session = Store.QuerySession())
            {
                actuals = func(session.Query<Target>()).ToList().OrderBy(x => x.Id).Select(x => x.Id).ToArray();
            }

            StoryTellerAssert.Fail(expected.Length != actuals.Length, $"Wrong number of results. Expected {expected.Length}, but got {actuals.Length}");

            for (int i = 0; i < actuals.Length; i++)
            {
                if (actuals[i] != expected[i]) return false;
            }

            return true;
        }


    }
}