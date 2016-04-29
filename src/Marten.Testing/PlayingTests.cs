using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Marten.Testing.Documents;
using Marten.Testing.Events.Projections;
using Marten.Testing.Fixtures;
using StoryTeller;
using StoryTeller.Engine;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class PlayingTests
    {

        public void run_st_spec()
        {
            using (var runner = new SpecRunner<NulloSystem>())
            {
                var results = runner.Run("Event Store/Projections/Inline Aggregation by Stream");


                runner.OpenResultsInBrowser();
            }
        }

        [Fact]
        public void fetch_index_definitions()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<User>().Searchable(x => x.UserName);
            }))
            {
                store.BulkInsert(new User[] {new User {UserName = "foo"}, new User { UserName = "bar" }, });
            }
        }

        [Fact]
        public void try_some_linq_queries()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();
                store.BulkInsert(Target.GenerateRandomData(200).ToArray());

                using (var session = store.LightweightSession())
                {
                    Debug.WriteLine(session.Query<Target>().Where(x => x.Double > 1.0).Count());
                    Debug.WriteLine(session.Query<Target>().Where(x => x.Double > 1.0 && x.Double < 33).Count());
                }
            }
        }

        public void try_out_select()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();
                store.Advanced.Clean.CompletelyRemoveAll();
                store.BulkInsert(Target.GenerateRandomData(200).ToArray());

                using (var session = store.QuerySession())
                {
                    session.Query<Target>().Select(x => x.Double).ToArray();
                }

            }
        }
    }


}