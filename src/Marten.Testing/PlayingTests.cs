using System.Diagnostics;
using System.Linq;
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
                var results = runner.Run("Event Store/Event Capture/Version a stream as part of event capture");


                runner.OpenResultsInBrowser();
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
    }


}