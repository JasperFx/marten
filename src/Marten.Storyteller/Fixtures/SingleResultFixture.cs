using System;
using System.Linq;
using Marten.Testing;
using StoryTeller;

namespace Marten.Storyteller.Fixtures
{
    public abstract class SingleResultFixture : MartenFixture
    {
        protected QueryList<IQueryable<Target>, Target> Queries = new QueryList<IQueryable<Target>, Target>(nameof(Queries), "");


        protected SingleResultFixture(string title)
        {
            Title = title;
            Queries.ReadFile(this);
        }

        public string RunQuery([SelectionList("Queries")] string Query)
        {
            using (var query = Store.QuerySession())
            {
                try
                {
                    var func = Queries.FuncFor(Query);
                    var target = func(query.Query<Target>());

                    if (target == null) return null;

                    return IdToName[target.Id];
                }
                catch (Exception e)
                {
                    return e.GetType().Name;
                }
            }
        }
    }
}